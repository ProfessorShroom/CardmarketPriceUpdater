using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.IO;

// Copyright © Charlie Howard 2025-2026 All rights reserved

namespace Cardmarket_Price_Updater.Core
{
    public static class CollectionWriter
    {
        private static readonly HashSet<string> BackedUp = new(StringComparer.Ordinal);
        private static readonly object BackupLock = new();

        public static CardEntry Update(string path, CardEntry card, string edition, decimal? boughtFor, bool pulled,
            int backupRetentionCount, Action<string>? log = null)
        {
            edition = edition.Trim();

            return Modify(path, card, backupRetentionCount, log, (ws, Col) =>
            {
                int editionCol = Col("edition"), boughtCol = Col("bought"), ownCol = Col("own");

                if (editionCol == -1)
                    throw new InvalidDataException("This spreadsheet has no Edition column to save to.");
                if (pulled) boughtFor = null;
                if (boughtCol == -1 && (boughtFor != card.BoughtFor || pulled != card.Pulled))
                    throw new InvalidDataException("This spreadsheet has no Bought For column to save to.");

                if (edition.Length == 0) ws.Clear(card.Row, editionCol);
                else ws.SetText(card.Row, editionCol, edition);

                if (boughtCol != -1)
                {
                    if (pulled) ws.SetText(card.Row, boughtCol, CollectionReader.PulledText);
                    else if (boughtFor is { } b) ws.SetNumber(card.Row, boughtCol, b);
                    else ws.Clear(card.Row, boughtCol);
                }

                string ownText = edition.Length == 0 ? string.Empty
                    : CollectionReader.IsCross(edition) ? "✖" : "✔";
                if (ownCol != -1 && !ws.HasFormula(card.Row, ownCol))
                {
                    if (ownText.Length == 0) ws.Clear(card.Row, ownCol);
                    else ws.SetText(card.Row, ownCol, ownText);
                }

                log?.Invoke($"Saved row {card.Row}: Edition \"{edition}\", Bought For {(pulled ? CollectionReader.PulledText : boughtFor?.ToString("0.00") ?? "blank")}.");

                return card with
                {
                    Edition = edition,
                    OwnText = ownCol == -1 ? card.OwnText : ownText,
                    BoughtFor = boughtCol == -1 ? card.BoughtFor : boughtFor,
                    Pulled = boughtCol == -1 ? card.Pulled : pulled,
                    Status = CollectionReader.GetStatus(edition, string.Empty)
                };
            });
        }

        public static CardEntry SetImage(string path, CardEntry card, byte[] image,
            int backupRetentionCount, Action<string>? log = null)
        {
            if (!ImageInfo.IsImage(image))
                throw new InvalidDataException("That file isn't a JPG or PNG image.");

            var updated = Modify(path, card, backupRetentionCount, log, (ws, Col) =>
            {
                int imgCol = Col("image");
                if (imgCol == -1)
                    throw new InvalidDataException("This spreadsheet has no Image column to put the picture in.");

                ws.SetImage(card.Row, imgCol, image);
                log?.Invoke($"Added image to row {card.Row}.");
                return card with { Image = image };
            });

            SaveToImageCache(card, image);
            return updated;
        }

        private static void SaveToImageCache(CardEntry card, byte[] image)
        {
            if (card.CardmarketId is not { } pid)
                return;

            try
            {
                var game = GameTypeExtensions.ParseGame(card.Game) ?? AppConfig.Load().DefaultGame;
                string dir = Path.Combine(AppConfig.GetConfigDirectory(), "ImageCache");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, $"{game}{pid}.img"), image);
            }
            catch
            {
            }
        }

        private static CardEntry Modify(string path, CardEntry card, int backupRetentionCount, Action<string>? log,
            Func<ISheet, Func<string, int>, CardEntry> change)
        {
            ExcelPackage.License.SetNonCommercialPersonal("Charlie Howard");

            EnsureNotOpenElsewhere(path);

            string full = Path.GetFullPath(path);
            lock (BackupLock)
            {
                if (!BackedUp.Contains(full))
                {
                    Backups.Create(path, backupRetentionCount, log);
                    BackedUp.Add(full);
                }
            }

            bool isOds = string.Equals(Path.GetExtension(path), ".ods", StringComparison.OrdinalIgnoreCase);
            string temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + (isOds ? ".ods" : ".xlsx"));
            File.Copy(path, temp, true);

            try
            {
                using var pkg = isOds ? null : new ExcelPackage(new FileInfo(temp));
                var ods = isOds ? OdsWorkbook.Load(temp) : null;
                ISheet ws = isOds ? ods!.FirstSheet : new EpplusSheet(pkg!.Workbook.Worksheets[0]);

                var header = CollectionReader.FindHeader(ws)
                    ?? throw new InvalidDataException("Could not find the column headers in the spreadsheet.");

                int Col(string key) => header.Cols.TryGetValue(key, out int c) ? c : -1;
                int pidCol = Col("pid"), setCol = Col("setcode");

                string pidNow = ws.GetText(card.Row, pidCol).Trim();
                string setNow = setCol == -1 ? string.Empty : ws.GetText(card.Row, setCol).Trim();
                string pidThen = card.CardmarketId?.ToString() ?? string.Empty;
                if (pidNow != pidThen || setNow != card.SetCode)
                    throw new InvalidOperationException(
                        "The spreadsheet has changed since it was opened, so nothing was saved. Press Reload and try again.");

                var result = change(ws, Col);

                if (ods != null)
                {
                    ods.Save(temp);
                }
                else
                {
                    XlsxFormulas.Recalculate(pkg!);
                    pkg!.Save();
                }
                File.Copy(temp, path, true);

                return result;
            }
            finally
            {
                try { File.Delete(temp); } catch { }
            }
        }

        private static void EnsureNotOpenElsewhere(string path)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
            string name = Path.GetFileName(path);

            foreach (var lockName in new[] { $".~lock.{name}#", $"~${name}" })
            {
                string lockPath = Path.Combine(dir, lockName);
                if (File.Exists(lockPath))
                    throw new IOException(
                        $"{name} looks like it's open in LibreOffice or Excel. Close it there first so your change isn't overwritten. " +
                        $"(If it isn't open, delete the leftover lock file: {lockPath})");
            }
        }
    }
}
