using System;
using System.IO;
using System.Linq;

// Copyright © Charlie Howard 2025-2026 All rights reserved

namespace Cardmarket_Price_Updater.Core
{
    public static class Backups
    {
        public static string Create(string workbookPath, int retentionCount, Action<string>? log = null)
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(workbookPath));
            dir ??= Directory.GetCurrentDirectory();
            string backupDir = Path.Combine(dir, "Backups");
            Directory.CreateDirectory(backupDir);

            string fileName = Path.GetFileName(workbookPath);
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string backupPath = Path.Combine(backupDir, $"{fileName}.{timestamp}.bak");

            File.Copy(workbookPath, backupPath, true);
            log?.Invoke($"Backup created: {backupPath}");

            if (retentionCount <= 0)
                return backupPath;

            var oldBackups = new DirectoryInfo(backupDir)
                .GetFiles($"{fileName}.*.bak")
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(retentionCount)
                .ToList();

            foreach (var old in oldBackups)
            {
                try
                {
                    old.Delete();
                    log?.Invoke($"Pruned old backup: {old.Name}");
                }
                catch (Exception ex)
                {
                    log?.Invoke($"Could not prune old backup {old.Name}: {ex.Message}");
                }
            }

            return backupPath;
        }
    }
}
