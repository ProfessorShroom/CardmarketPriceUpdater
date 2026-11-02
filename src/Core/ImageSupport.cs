using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Security.Cryptography;
using System.Threading.Tasks;

// Copyright © Charlie Howard 2025-2026 All rights reserved

namespace Cardmarket_Price_Updater.Core
{
    internal static class ImageLayout
    {
        public const double RowHeightCm = 1.8;
        public const double ImageHeightCm = 1.7;
        public const double ColumnWidthCm = 1.4;
        public const double DefaultAspect = 0.686;
        public const double CmPerInch = 2.54;
    }

    internal static class ImageInfo
    {
        public static bool IsJpeg(byte[] d) => d.Length > 3 && d[0] == 0xFF && d[1] == 0xD8;

        public static bool IsPng(byte[] d) =>
            d.Length > 24 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47;

        public static bool IsImage(byte[] d) => IsJpeg(d) || IsPng(d);

        public static string Extension(byte[] d) => IsPng(d) ? "png" : "jpg";

        public static string MimeType(byte[] d) => IsPng(d) ? "image/png" : "image/jpeg";

        public static string Hash(byte[] d) =>
            Convert.ToHexString(SHA1.HashData(d))[..16].ToLowerInvariant();

        public static bool TryGetSize(byte[] d, out int width, out int height)
        {
            width = height = 0;

            if (IsPng(d))
            {
                width = (d[16] << 24) | (d[17] << 16) | (d[18] << 8) | d[19];
                height = (d[20] << 24) | (d[21] << 16) | (d[22] << 8) | d[23];
                return width > 0 && height > 0;
            }

            if (IsJpeg(d))
            {
                int i = 2;
                while (i + 9 < d.Length)
                {
                    if (d[i] != 0xFF) { i++; continue; }

                    byte marker = d[i + 1];
                    if (marker == 0xFF) { i++; continue; }
                    if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                    {
                        i += 2;
                        continue;
                    }

                    int len = (d[i + 2] << 8) | d[i + 3];

                    if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                    {
                        height = (d[i + 5] << 8) | d[i + 6];
                        width = (d[i + 7] << 8) | d[i + 8];
                        return width > 0 && height > 0;
                    }

                    i += 2 + len;
                }
            }

            return false;
        }

        public static (double WidthCm, double HeightCm) FitCm(byte[] d)
        {
            double aspect = TryGetSize(d, out int w, out int h) ? (double)w / h : ImageLayout.DefaultAspect;
            double hCm = ImageLayout.ImageHeightCm;
            double wCm = hCm * aspect;

            double maxW = ImageLayout.ColumnWidthCm - 0.2;
            if (wCm > maxW)
            {
                wCm = maxW;
                hCm = wCm / aspect;
            }
            return (wCm, hCm);
        }
    }

    internal sealed class CardImageProvider
    {
        private const string BaseUrl = "https://product-images.s3.cardmarket.com";

        private const string PromoFolder = "PROM";

        private const string UserAgent =
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";

        private readonly HttpClient _http;
        private readonly Action<string>? _log;
        private readonly string _cacheDir;
        private readonly Dictionary<(GameType Game, string Prefix), List<string>> _knownFolders = new();
        private bool _warnedNetwork;
        private bool _warnedBlocked;
        private bool _hintedAliases;
        private readonly Dictionary<string, string[]> _aliases;

        private static readonly Dictionary<string, string[]> BuiltInAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["JMP"] = new[] { "JUMP" },
            ["LC01"] = new[] { "LC25" },
            ["711D"] = new[] { "711C" },
        };

        public CardImageProvider(HttpClient http, Action<string>? log = null, string? cacheDir = null)
        {
            _http = http;
            _log = log;
            _cacheDir = cacheDir ?? Path.Combine(AppConfig.GetConfigDirectory(), "ImageCache");
            _aliases = LoadAliases();
        }

        private static string AliasFilePath => Path.Combine(AppConfig.GetConfigDirectory(), "ImageFolders.json");

        private Dictionary<string, string[]> LoadAliases()
        {
            var map = new Dictionary<string, string[]>(BuiltInAliases, StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = AliasFilePath;
                if (!File.Exists(path))
                    return map;

                using var doc = JsonDocument.Parse(File.ReadAllText(path),
                    new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var folders = new List<string>();
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        folders.Add(prop.Value.GetString() ?? "");
                    else if (prop.Value.ValueKind == JsonValueKind.Array)
                        foreach (var e in prop.Value.EnumerateArray())
                            if (e.ValueKind == JsonValueKind.String) folders.Add(e.GetString() ?? "");

                    folders.RemoveAll(string.IsNullOrWhiteSpace);
                    if (folders.Count > 0)
                        map[prop.Name.Trim()] = folders.Select(f => f.Trim()).ToArray();
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Could not read ImageFolders.json: {ex.Message}");
            }
            return map;
        }

        private static int CategoryId(GameType game) => game switch
        {
            GameType.MTG => 1,
            GameType.Pokemon => 51,
            _ => 5
        };

        public async Task<byte[]?> GetAsync(GameType game, int productId, string setCode)
        {
            string cachePath = Path.Combine(_cacheDir, $"{game}{productId}.img");
            try
            {
                if (File.Exists(cachePath))
                {
                    var cached = await File.ReadAllBytesAsync(cachePath);
                    if (ImageInfo.IsImage(cached))
                        return cached;
                }
            }
            catch
            {
            }

            int category = CategoryId(game);
            string prefix = FolderPrefix(setCode);

            var tried = new List<string>();
            foreach (string folder in FolderCandidates(game, setCode, prefix))
            {
                string url = $"{BaseUrl}/{category}/{folder}/{productId}/{productId}.jpg";

                var (bytes, status) = await TryDownloadAsync(url);
                tried.Add($"{folder} [{status}]");

                if (bytes == null)
                    continue;

                if (prefix.Length > 0)
                {
                    if (!_knownFolders.TryGetValue((game, prefix), out var list))
                        _knownFolders[(game, prefix)] = list = new List<string>();
                    if (!list.Contains(folder, StringComparer.OrdinalIgnoreCase))
                        list.Add(folder);
                }
                try
                {
                    Directory.CreateDirectory(_cacheDir);
                    await File.WriteAllBytesAsync(cachePath, bytes);
                }
                catch
                {
                }
                return bytes;
            }

            _log?.Invoke(tried.Count == 0
                ? $"No image for {productId}: no Set Code to work out its image folder from."
                : $"No image found for {productId} (tried folders: {string.Join(", ", tried)}).");

            if (tried.Count > 0 && !_hintedAliases)
            {
                _hintedAliases = true;
                _log?.Invoke($"Tip: if you know the real folder (from the card's image URL), map the set code to it in {AliasFilePath}, e.g. {{ \"{prefix}\": \"FOLDER\" }}");
            }
            return null;
        }

        private async Task<(byte[]? Bytes, string Status)> TryDownloadAsync(string url)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                req.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/jpeg,image/*;q=0.8,*/*;q=0.5");
                req.Headers.TryAddWithoutValidation("Referer", "https://www.cardmarket.com/");

                using var resp = await _http.SendAsync(req);
                int code = (int)resp.StatusCode;

                if (!resp.IsSuccessStatusCode)
                {
                    bool fromS3 = resp.Headers.Server.ToString().Contains("AmazonS3", StringComparison.OrdinalIgnoreCase);
                    bool plainMiss = code == 404 || (code == 403 && fromS3);
                    if (!plainMiss && !_warnedBlocked)
                    {
                        _warnedBlocked = true;
                        _log?.Invoke($"Image server returned HTTP {code} (server: {resp.Headers.Server}) - Cardmarket may be blocking the request.");
                    }
                    return (null, code.ToString());
                }

                var bytes = await resp.Content.ReadAsByteArrayAsync();
                return ImageInfo.IsImage(bytes) ? (bytes, "200") : (null, "not an image");
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                if (!_warnedNetwork)
                {
                    _warnedNetwork = true;
                    _log?.Invoke($"Image download problem: {ex.Message}");
                }
                return (null, "network error");
            }
        }

        // "LOB-E056" -> "LOB", "BS 4" -> "BS", "NKC1-EN002" -> "NKC1"
        private static string FolderPrefix(string setCode)
        {
            setCode = setCode.Trim();
            int end = setCode.IndexOfAny(new[] { '-', ' ', '/' });
            return end > 0 ? setCode[..end] : setCode;
        }

        private IEnumerable<string> FolderCandidates(GameType game, string setCode, string prefix)
        {
            setCode = setCode.Trim();
            if (setCode.Length == 0)
                yield break;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (_knownFolders.TryGetValue((game, prefix), out var known))
                foreach (var k in known.ToArray())
                    if (seen.Add(k))
                        yield return k;

            if (_aliases.TryGetValue(prefix, out var mapped))
            {
                foreach (var folder in mapped)
                {
                    if (seen.Add(folder))
                        yield return folder;
                }
            }

            // LOB-E056 -> LOB
            if (seen.Add(prefix))
                yield return prefix;

            // YAC1-JP001 -> YAC1-JP
            int dash = setCode.IndexOf('-');
            if (dash > 0)
            {
                string rest = setCode[(dash + 1)..];
                int n = 0;
                while (n < rest.Length && char.IsLetter(rest[n])) n++;
                if (n > 0)
                {
                    string withLang = $"{prefix}-{rest[..n]}";
                    if (seen.Add(withLang))
                        yield return withLang;
                }
            }

            if (seen.Add(PromoFolder))
                yield return PromoFolder;
        }
    }
}
