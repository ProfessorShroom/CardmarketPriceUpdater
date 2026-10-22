using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

// Copyright © Charlie Howard 2025-2026 All rights reserved


namespace Cardmarket_Price_Updater.Core
{
    public record UpdateInfo(Version Version, string DownloadUrl, string ChangelogUrl, bool Mandatory);

    public static class UpdateChecker
    {
        public static async Task<UpdateInfo?> CheckAsync(string updateXmlUrl, Version currentVersion)
        {
            try
            {
                using var http = new HttpClient();
                string xml = await RetryPolicy.RunAsync(
                    () => http.GetStringAsync(updateXmlUrl),
                    maxAttempts: 2, initialDelaySeconds: 1);

                var doc = XDocument.Parse(xml);
                var item = doc.Root;
                if (item is null) return null;

                string? versionText = item.Element("version")?.Value;
                string? url = item.Element("url")?.Value;
                string? changelog = item.Element("changelog")?.Value;
                bool mandatory = bool.TryParse(item.Element("mandatory")?.Value, out var m) && m;

                if (versionText is null || url is null) return null;
                if (!Version.TryParse(versionText, out var remoteVersion)) return null;

                if (remoteVersion <= currentVersion) return null;

                return new UpdateInfo(remoteVersion, url, changelog ?? string.Empty, mandatory);
            }
            catch
            {
                return null;
            }
        }
    }
}
