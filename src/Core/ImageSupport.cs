using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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

        private readonly HttpClient _http;
        private readonly Action<string>? _log;
        private readonly string _cacheDir;
        private readonly Dictionary<(GameType Game, string Prefix), string> _knownFolders = new();
        private bool _warnedNetwork;

        public CardImageProvider(HttpClient http, Action<string>? log = null, string? cacheDir = null)
        {
            _http = http;
            _log = log;
            _cacheDir = cacheDir ?? Path.Combine(AppConfig.GetConfigDirectory(), "ImageCache");
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
                tried.Add(folder);

                var bytes = await TryDownloadAsync(url);
                if (bytes == null)
                    continue;

                _knownFolders[(game, prefix)] = folder;
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
            return null;
        }

        private async Task<byte[]?> TryDownloadAsync(string url)
        {
            try
            {
                using var resp = await _http.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                    return null;

                var bytes = await resp.Content.ReadAsByteArrayAsync();
                return ImageInfo.IsImage(bytes) ? bytes : null;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                if (!_warnedNetwork)
                {
                    _warnedNetwork = true;
                    _log?.Invoke($"Image download problem: {ex.Message}");
                }
                return null;
            }
        }

        private static string FolderPrefix(string setCode)
        {
            setCode = setCode.Trim();
            int dash = setCode.IndexOf('-');
            return dash > 0 ? setCode[..dash] : setCode;
        }

        private IEnumerable<string> FolderCandidates(GameType game, string setCode, string prefix)
        {
            setCode = setCode.Trim();
            if (setCode.Length == 0)
                yield break;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (_knownFolders.TryGetValue((game, prefix), out var known) && seen.Add(known))
                yield return known;

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
        }
    }
}
