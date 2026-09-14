using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Cardmarket_Price_Updater.Core;
using CardPriceUpdaterGui;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

// Copyright © Charlie Howard 2026 All rights reserved.

namespace Cardmarket_Price_Updater
{
    public partial class MainWindow : Window
    {
        private sealed record PriceTypeItem(PriceType Value, string Text)
        {
            public override string ToString() => Text;
        }

        private AppConfig _config = AppConfig.Load();
        private string? _updateDownloadUrl;
        private bool _suppressCurrencyEvents;

        public MainWindow()
        {
            InitializeComponent();

            // ---------------- PRICE TYPE DROPDOWN ----------------
            var items = new List<PriceTypeItem>
            {
                new(PriceType.trend, "Trend"),
                new(PriceType.avg7, "7-Day Average"),
                new(PriceType.avg30, "30-Day Average"),
            };
            PriceTypeCombo.ItemsSource = items;
            PriceTypeCombo.SelectedItem = items.FirstOrDefault(i => i.Value == _config.PriceType) ?? items[2];

            SetCurrencyChecked(_config.CurrencyMode);
            CurrencyGbp.IsCheckedChanged += (_, __) => OnCurrencyToggled(CurrencyGbp, CurrencyMode.GBP);
            CurrencyEur.IsCheckedChanged += (_, __) => OnCurrencyToggled(CurrencyEur, CurrencyMode.EUR);
            CurrencyUsd.IsCheckedChanged += (_, __) => OnCurrencyToggled(CurrencyUsd, CurrencyMode.USD);

            // ---------------- VERSION LABEL ----------------
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(4) ?? "0.0.0.0";
            VersionLink.Content = $"Version {version}";
            VersionLink.Click += (_, __) => OpenUrl(_updateDownloadUrl ?? "https://professorshroom.com/projects/Cardmarket_Price_Updater/#changelog");

            StartButton.Click += StartButton_Click;

            // ---------------- CLOSE BUTTON ----------------
            CloseButton.Click += (_, __) => Close();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _ = CheckForUpdateAsync();
            }
        }

        private async Task CheckForUpdateAsync()
        {
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
            var update = await UpdateChecker.CheckAsync(
                "https://raw.githubusercontent.com/ProfessorShroom/CardmarketPriceUpdater/refs/heads/main/update.xml",
                current);

            if (update is null) return;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _updateDownloadUrl = update.DownloadUrl;
                AppendLog($"Update available: v{update.Version} - {update.DownloadUrl}");
                VersionLink.Content = $"Version {current.ToString(4)}  (v{update.Version} available - click to download)";
            });
        }

        private void SetCurrencyChecked(CurrencyMode mode)
        {
            _suppressCurrencyEvents = true;
            CurrencyGbp.IsChecked = mode == CurrencyMode.GBP || mode == CurrencyMode.AUTO;
            CurrencyEur.IsChecked = mode == CurrencyMode.EUR;
            CurrencyUsd.IsChecked = mode == CurrencyMode.USD;
            _suppressCurrencyEvents = false;
        }

        private CurrencyMode SelectedCurrency =>
            CurrencyEur.IsChecked == true ? CurrencyMode.EUR :
            CurrencyUsd.IsChecked == true ? CurrencyMode.USD :
            CurrencyMode.GBP;

        private void OnCurrencyToggled(ToggleButton source, CurrencyMode mode)
        {
            if (_suppressCurrencyEvents) return;
            if (source.IsChecked != true)
            {
                return;
            }

            _suppressCurrencyEvents = true;
            CurrencyGbp.IsChecked = mode == CurrencyMode.GBP;
            CurrencyEur.IsChecked = mode == CurrencyMode.EUR;
            CurrencyUsd.IsChecked = mode == CurrencyMode.USD;
            _suppressCurrencyEvents = false;
        }

        // ---------------- LOGGING ----------------
        private void AppendLog(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                OutputBox.Text += line + Environment.NewLine;
                OutputBox.CaretIndex = OutputBox.Text.Length;
            });
        }

        // ---------------- START BUTTON ----------------
        private async void StartButton_Click(object? sender, RoutedEventArgs e)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select spreadsheet(s) to update",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Excel (*.xlsx)") { Patterns = new[] { "*.xlsx" } }
                }
            });

            if (files.Count == 0)
                return;

            StartButton.IsEnabled = false;
            try
            {
                OutputBox.Text = string.Empty;
                var selectedPriceType = ((PriceTypeItem)PriceTypeCombo.SelectedItem!).Value;
                var selectedCurrency = SelectedCurrency;

                _config.CurrencyMode = selectedCurrency;
                _config.PriceType = selectedPriceType;
                _config.Save();

                foreach (var file in files)
                {
                    string path = file.Path.LocalPath;
                    AppendLog($"Starting Update: {path}");

                    await Task.Run(() =>
                    {
                        new PriceUpdater(AppendLog, _config, selectedCurrency, selectedPriceType).Run(path);
                    });
                }

                AppendLog("All files complete.");
            }
            catch (Exception ex)
            {
                AppendLog("ERROR: " + ex);
            }
            finally
            {
                StartButton.IsEnabled = true;
            }
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch
            {
                try
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                        Process.Start("xdg-open", url);
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                        Process.Start("open", url);
                }
                catch
                {
                }
            }
        }
    }
}
