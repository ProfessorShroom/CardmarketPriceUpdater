using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

// Copyright © Charlie Howard 2025-2026 All rights reserved

namespace Cardmarket_Price_Updater.Core
{
    public enum CardStatus
    {
        Owned,

        NeedsUpgrade,

        NotOwned
    }

    public sealed record CardEntry(
        int Row,
        string Name,
        string SetName,
        string SetCode,
        string Rarity,
        string Game,
        string ReleaseDate,
        string Edition,
        string OwnText,
        decimal? Price,
        string PriceUpdated,
        decimal? BoughtFor,
        int? CardmarketId,
        CardStatus Status,
        byte[]? Image,
        bool Pulled = false);

    public sealed record CollectionData(
        string FilePath,
        string SheetName,
        string CurrencySymbol,
        IReadOnlyList<CardEntry> Cards);

    public static class CollectionReader
    {
        public static readonly IReadOnlyList<string> StandardEditions = new[]
        {
            "✔", "1st Edition", "LIMITED EDITION", "Unlimited",
            "PSA10", "PSA9", "PSA8", "PSA7", "PSA6", "PSA5", "PSA4", "PSA3", "PSA2", "PSA1",
            "✖"
        };

        public static CollectionData Read(string path)
        {
            ExcelPackage.License.SetNonCommercialPersonal("Charlie Howard");

            byte[] bytes = File.ReadAllBytes(path);
            bool isOds = string.Equals(Path.GetExtension(path), ".ods", StringComparison.OrdinalIgnoreCase);

            if (isOds)
            {
                var wb = OdsWorkbook.Load(bytes);
                return ReadSheet(path, wb.FirstSheetName, wb.FirstSheet);
            }

            using var pkg = new ExcelPackage(new MemoryStream(bytes));
            var ws = pkg.Workbook.Worksheets[0];
            return ReadSheet(path, ws.Name, new EpplusSheet(ws));
        }

        private static CollectionData ReadSheet(string path, string sheetName, ISheet ws)
        {
            var header = FindHeader(ws)
                ?? throw new InvalidDataException(
                    "Could not find the column headers (Card Price and Cardmarket ID) in the first 20 rows.");

            int Col(string key) => header.Cols.TryGetValue(key, out int c) ? c : -1;
            string Get(int r, string key) { int c = Col(key); return c == -1 ? string.Empty : ws.GetText(r, c).Trim(); }

            int imgCol = Col("image");
            var cards = new List<CardEntry>();
            var imageCache = ImageCacheLookup.Create();

            for (int r = header.Row + 1; r <= ws.LastRow; r++)
            {
                string pidText = Get(r, "pid");
                string setCode = Get(r, "setcode");
                string setName = Get(r, "setname");
                string name = Get(r, "name");

                bool hasPid = int.TryParse(pidText, out int pid);
                if (!hasPid && setCode.Length == 0 && name.Length == 0)
                    continue;

                string edition = Get(r, "edition");
                string own = Get(r, "own");
                string game = Get(r, "game");

                byte[]? image = null;
                if (imgCol != -1)
                {
                    try { image = ws.GetImage(r, imgCol); } catch { }
                }
                if (image == null && hasPid)
                    image = imageCache.Find(game, pid);

                cards.Add(new CardEntry(
                    Row: r,
                    Name: name,
                    SetName: setName,
                    SetCode: setCode,
                    Rarity: Get(r, "rarity"),
                    Game: game,
                    ReleaseDate: Get(r, "release"),
                    Edition: edition,
                    OwnText: own,
                    Price: ParseMoney(Get(r, "price")),
                    PriceUpdated: Get(r, "updated"),
                    BoughtFor: ParseMoney(Get(r, "bought")),
                    Pulled: IsPulled(Get(r, "bought")),
                    CardmarketId: hasPid ? pid : null,
                    Status: GetStatus(edition, own),
                    Image: image));
            }

            return new CollectionData(path, sheetName, header.Currency, cards);
        }

        public static CardStatus GetStatus(string edition, string own)
        {
            string e = edition.Trim();
            if (e.Length == 0)
                e = own.Trim();

            if (e.Length == 0 || IsCross(e))
                return CardStatus.NotOwned;

            if (e.Equals("Unlimited", StringComparison.OrdinalIgnoreCase))
                return CardStatus.NeedsUpgrade;

            if (e.StartsWith("PSA", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(e[3..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int grade)
                && grade < 9)
                return CardStatus.NeedsUpgrade;

            return CardStatus.Owned;
        }

        public const string PulledText = "Pulled";

        public static bool IsPulled(string text) =>
            text.Trim().Equals(PulledText, StringComparison.OrdinalIgnoreCase);

        internal static bool IsCross(string s) =>
            s is "✖" or "✗" or "✘" or "x" or "X" or "×" or "❌"
            || s.Equals("no", StringComparison.OrdinalIgnoreCase);

        public static decimal? ParseMoney(string text)
        {
            var sb = new StringBuilder();
            foreach (char ch in text)
                if (char.IsDigit(ch) || ch == '.' || ch == ',' || ch == '-')
                    sb.Append(ch);

            string s = sb.ToString().Trim('-', '.', ',');
            if (s.Length == 0 || !s.Any(char.IsDigit))
                return null;

            bool negative = text.TrimStart().StartsWith('-') || text.Contains('(');
            int lastDot = s.LastIndexOf('.');
            int lastComma = s.LastIndexOf(',');

            if (lastDot >= 0 && lastComma >= 0)
            {
                s = lastComma > lastDot ? s.Replace(".", "").Replace(',', '.') : s.Replace(",", "");
            }
            else if (lastComma >= 0)
            {
                bool decimalComma = s.Count(c => c == ',') == 1 && s.Length - lastComma - 1 is 1 or 2;
                s = decimalComma ? s.Replace(',', '.') : s.Replace(",", "");
            }
            else if (s.Count(c => c == '.') > 1)
            {
                s = s.Replace(".", "");
            }

            if (!decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                return null;
            return negative ? -d : d;
        }

        internal sealed record HeaderInfo(int Row, Dictionary<string, int> Cols, string Currency);

        internal static HeaderInfo? FindHeader(ISheet ws)
        {
            int maxRow = Math.Min(20, ws.LastRow);
            int maxCol = ws.LastCol;

            for (int r = 1; r <= maxRow; r++)
            {
                var cols = new Dictionary<string, int>();
                string currency = string.Empty;

                void Set(string key, int c) { if (!cols.ContainsKey(key)) cols[key] = c; }

                for (int c = 1; c <= maxCol; c++)
                {
                    string raw = ws.GetText(r, c).Trim();
                    string h = raw.ToLowerInvariant();
                    if (h.Length == 0) continue;

                    if (h.Contains("card price"))
                    {
                        Set("price", c);
                        currency = CurrencyFromHeader(raw);
                    }
                    else if (h.Contains("price updated")) Set("updated", c);
                    else if (h.Contains("cardmarket id")) Set("pid", c);
                    else if (h.Contains("game")) Set("game", c);
                    else if (h.Contains("image")) Set("image", c);
                    else if (h.Contains("set code")) Set("setcode", c);
                    else if (h.Contains("set name")) Set("setname", c);
                    else if (h.Contains("release")) Set("release", c);
                    else if (h.Contains("rarity")) Set("rarity", c);
                    else if (h.Contains("edition")) Set("edition", c);
                    else if (h.StartsWith("own")) Set("own", c);
                    else if (h.Contains("bought")) Set("bought", c);
                    else if (h is "name" or "card" || h.Contains("card name")) Set("name", c);
                }

                if (cols.ContainsKey("price") && cols.ContainsKey("pid"))
                    return new HeaderInfo(r, cols, currency);
            }
            return null;
        }

        private static string CurrencyFromHeader(string header)
        {
            int open = header.LastIndexOf('(');
            int close = header.LastIndexOf(')');
            if (open >= 0 && close > open + 1)
                return header[(open + 1)..close].Trim();
            return string.Empty;
        }

        private sealed class ImageCacheLookup
        {
            private readonly string _dir;
            private readonly GameType _defaultGame;

            private ImageCacheLookup(string dir, GameType defaultGame)
            {
                _dir = dir;
                _defaultGame = defaultGame;
            }

            public static ImageCacheLookup Create()
            {
                string dir;
                try { dir = Path.Combine(AppConfig.GetConfigDirectory(), "ImageCache"); }
                catch { dir = string.Empty; }
                return new ImageCacheLookup(dir, AppConfig.Load().DefaultGame);
            }

            public byte[]? Find(string gameText, int pid)
            {
                if (_dir.Length == 0 || !Directory.Exists(_dir))
                    return null;

                var game = GameTypeExtensions.ParseGame(gameText) ?? _defaultGame;
                try
                {
                    string p = Path.Combine(_dir, $"{game}{pid}.img");
                    if (!File.Exists(p)) return null;
                    var data = File.ReadAllBytes(p);
                    return ImageInfo.IsImage(data) ? data : null;
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
