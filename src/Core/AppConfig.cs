using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

// Copyright © Charlie Howard 2025-2026 All rights reserved

namespace Cardmarket_Price_Updater.Core
{
    public class AppConfig
    {
        [JsonPropertyName("currencyMode")]
        public CurrencyMode CurrencyMode { get; set; } = CurrencyMode.AUTO;

        [JsonPropertyName("priceType")]
        public PriceType PriceType { get; set; } = PriceType.avg30;

        [JsonPropertyName("defaultGame")]
        public GameType DefaultGame { get; set; } = GameTypeExtensions.Default;

        [JsonPropertyName("maxRetries")]
        public int MaxRetries { get; set; } = 3;

        [JsonPropertyName("retryDelaySeconds")]
        public double RetryDelaySeconds { get; set; } = 2;

        [JsonPropertyName("backupRetentionCount")]
        public int BackupRetentionCount { get; set; } = 5;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static string GetConfigDirectory()
        {
            string baseDir = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.Create);

            string dir = Path.Combine(baseDir, "CardmarketPriceUpdater");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string GetDefaultPath() =>
            Path.Combine(GetConfigDirectory(), "config.json");

        public static AppConfig Load(string? path = null)
        {
            path ??= GetDefaultPath();
            try
            {
                if (!File.Exists(path))
                    return new AppConfig();

                string json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                return config ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        public void Save(string? path = null)
        {
            path ??= GetDefaultPath();
            string json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(path, json);
        }
    }
}
