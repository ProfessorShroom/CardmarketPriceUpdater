using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform.Storage;
using Cardmarket_Price_Updater.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

// Copyright © Charlie Howard 2025-2026 All rights reserved

namespace Cardmarket_Price_Updater
{
    public sealed class CardTile : INotifyPropertyChanged
    {
        private bool _isSelected;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        private static readonly IBrush OwnedBrush = new ImmutableSolidColorBrush(Color.Parse("#00B050"));
        private static readonly IBrush UpgradeBrush = new ImmutableSolidColorBrush(Color.Parse("#0070C0"));
        private static readonly IBrush NotOwnedBrush = new ImmutableSolidColorBrush(Color.Parse("#C00000"));
        private static readonly IBrush GradedBrush = new ImmutableSolidColorBrush(Color.Parse("#7030A0"));
        private static readonly IBrush WhiteBrush = new ImmutableSolidColorBrush(Colors.White);
        private static readonly IBrush GoldBrush = new ImmutableSolidColorBrush(Color.Parse("#FFD966"));
        private static readonly IBrush NoBrush = new ImmutableSolidColorBrush(Colors.Transparent);

        public CardTile(CardEntry card, string currency)
        {
            Card = card;

            Title = FirstNonEmpty(card.Name, card.SetCode, card.SetName, $"Row {card.Row}");
            Subtitle = card.Name.Length > 0
                ? Join(" · ", card.SetCode, card.SetName)
                : (card.SetCode.Length > 0 ? card.SetName : string.Empty);
            Meta = Join(" · ", card.Rarity, card.Game, card.ReleaseDate);

            PriceText = card.Price is { } p ? Money(p, currency) : "No price";
            BoughtText = card.Pulled ? "Pulled"
                : card.BoughtFor is { } b ? $"Bought for {Money(b, currency)}"
                : string.Empty;

            EditionText = card.Status switch
            {
                CardStatus.NotOwned => "Not owned",
                _ when card.Edition.Length > 0 => card.Edition,
                CardStatus.NeedsUpgrade => "Upgrade",
                _ => "Owned"
            };

            string grade = card.Edition.Replace(" ", "").ToUpperInvariant();
            bool psa10 = card.Status == CardStatus.Owned && grade == "PSA10";
            bool psa9 = card.Status == CardStatus.Owned && grade == "PSA9";

            StatusBrush = card.Status switch
            {
                CardStatus.NotOwned => NotOwnedBrush,
                CardStatus.NeedsUpgrade => UpgradeBrush,
                _ when psa10 || psa9 => GradedBrush,
                _ => OwnedBrush
            };
            BadgeForeground = psa10 ? GoldBrush : WhiteBrush;
            BadgeBorder = psa10 ? GoldBrush : NoBrush;

            string statusText = card.Status switch
            {
                CardStatus.Owned => "Owned",
                CardStatus.NeedsUpgrade => "Owned - needs upgrade",
                _ => "Not owned"
            };

            Tooltip = string.Join(Environment.NewLine, new[]
            {
                Join(" · ", card.Name, card.SetCode),
                card.SetName,
                Join(" · ", card.Rarity, card.Game),
                card.ReleaseDate.Length > 0 ? $"Released {card.ReleaseDate}" : "",
                $"{statusText}{(card.Edition.Length > 0 && card.Status != CardStatus.NotOwned ? $" ({card.Edition})" : "")}",
                card.PriceUpdated.Length > 0 ? $"Price updated {card.PriceUpdated}" : "",
                card.CardmarketId is { } id ? $"Cardmarket ID {id}" : "",
                $"Spreadsheet row {card.Row}"
            }.Where(s => s.Length > 0));

            if (card.Image != null)
            {
                try
                {
                    using var ms = new MemoryStream(card.Image);
                    Image = Bitmap.DecodeToWidth(ms, 360);
                }
                catch
                {
                    Image = null;
                }
            }

            SearchText = string.Join(' ', card.Name, card.SetName, card.SetCode, card.Rarity, card.Pulled ? "pulled" : "",
                card.Game, card.Edition, card.CardmarketId?.ToString() ?? "").ToLowerInvariant();
        }

        public CardEntry Card { get; }
        public string Title { get; }
        public string Subtitle { get; }
        public string Meta { get; }
        public string PriceText { get; }
        public string BoughtText { get; }
        public bool HasBought => BoughtText.Length > 0;
        public string EditionText { get; }
        public IBrush StatusBrush { get; }
        public IBrush BadgeForeground { get; }
        public IBrush BadgeBorder { get; }
        public string Tooltip { get; }
        public Bitmap? Image { get; }
        public bool HasImage => Image != null;
        internal string SearchText { get; }

        internal static string Money(decimal value, string currency)
        {
            string n = value.ToString("#,0.00", CultureInfo.InvariantCulture);
            if (currency.Length == 0) return n;
            return currency.Length == 1 ? currency + n : $"{n} {currency}";
        }

        private static string FirstNonEmpty(params string[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

        private static string Join(string sep, params string[] parts) =>
            string.Join(sep, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    public partial class CollectionView : UserControl
    {
        private const string AllGames = "All games";

        private sealed record SortItem(string Text, Func<IEnumerable<CardTile>, IEnumerable<CardTile>> Apply)
        {
            public override string ToString() => Text;
        }

        private readonly List<SortItem> _sorts = new()
        {
            new("Spreadsheet order", t => t.OrderBy(x => x.Card.Row)),
            new("Price: high to low", t => t.OrderByDescending(x => x.Card.Price ?? -1m)),
            new("Price: low to high", t => t.OrderBy(x => x.Card.Price ?? decimal.MaxValue)),
            new("Release date", t => t.OrderBy(x => ParseDate(x.Card.ReleaseDate) ?? DateTime.MaxValue)),
            new("Set name", t => t.OrderBy(x => x.Card.SetName, StringComparer.CurrentCultureIgnoreCase)
                                  .ThenBy(x => x.Card.SetCode, StringComparer.CurrentCultureIgnoreCase)),
            new("Status", t => t.OrderBy(x => x.Card.Status switch
            {
                CardStatus.NotOwned => 0,
                CardStatus.NeedsUpgrade => 1,
                _ => 2
            }).ThenByDescending(x => x.Card.Price ?? -1m)),
        };

        private CollectionData? _data;
        private List<CardTile> _tiles = new();
        private string? _path;

        public string? CurrentPath => _path;
        private bool _suppressRefresh;
        private CardTile? _selected;
        private bool _saving;

        public CollectionView()
        {
            InitializeComponent();

            SortCombo.ItemsSource = _sorts;
            SortCombo.SelectedIndex = 0;
            GameFilter.ItemsSource = new[] { AllGames };
            GameFilter.SelectedIndex = 0;

            OpenButton.Click += async (_, __) => await PickAndOpenAsync();
            ReloadButton.Click += async (_, __) => { if (_path != null) await LoadAsync(_path); };

            SearchBox.TextChanged += (_, __) => Refresh();
            GameFilter.SelectionChanged += (_, __) => Refresh();
            SortCombo.SelectionChanged += (_, __) => Refresh();
            ShowOwned.IsCheckedChanged += (_, __) => Refresh();
            ShowUpgrade.IsCheckedChanged += (_, __) => Refresh();
            ShowNotOwned.IsCheckedChanged += (_, __) => Refresh();

            OnlyUpgradeButton.Click += (_, __) => SetStatusFilter(false, true, false);
            OnlyNotOwnedButton.Click += (_, __) => SetStatusFilter(false, false, true);
            ShowAllButton.Click += (_, __) => SetStatusFilter(true, true, true);

            CardList.AddHandler(TappedEvent, (_, e) =>
            {
                if (e.Source is Control { DataContext: CardTile tile })
                    Select(tile);
            });

            EditorCloseButton.Click += (_, __) => Select(null);
            RevertButton.Click += (_, __) => FillEditor();
            SaveButton.Click += async (_, __) => await SaveAsync();
            ImageButton.Click += async (_, __) => await AddImageAsync();
            PulledCheck.IsCheckedChanged += (_, __) =>
            {
                bool pulled = PulledCheck.IsChecked == true;
                BoughtBox.IsEnabled = !pulled;
                if (pulled) BoughtBox.Text = string.Empty;
            };

            QuickTick.Click += (_, __) => SetEdition("✔");
            QuickFirst.Click += (_, __) => SetEdition("1st Edition");
            QuickLimited.Click += (_, __) => SetEdition("LIMITED EDITION");
            QuickUnlimited.Click += (_, __) => SetEdition("Unlimited");
            QuickCross.Click += (_, __) => SetEdition("✖");

            BoughtBox.KeyDown += async (_, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; await SaveAsync(); }
            };
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape && _selected != null) { e.Handled = true; Select(null); }
            };
        }

        private void Select(CardTile? tile)
        {
            if (_selected != null) _selected.IsSelected = false;
            _selected = tile;
            if (tile != null) tile.IsSelected = true;

            EditorPanel.IsVisible = tile != null;
            EditorMessage.Text = string.Empty;
            FillEditor();
        }

        private void FillEditor()
        {
            var tile = _selected;
            if (tile == null || _data == null) return;
            var card = tile.Card;

            EditorImage.Source = tile.Image;
            EditorImage.IsVisible = tile.HasImage;
            EditorNoImage.IsVisible = !tile.HasImage;
            ImageButton.Content = tile.HasImage ? "Replace image..." : "Add image...";
            EditorTitle.Text = tile.Title;
            EditorSubtitle.Text = tile.Subtitle;
            EditorMeta.Text = tile.Meta;
            EditorPrice.Text = tile.PriceText;

            var editions = CollectionReader.StandardEditions.ToList();
            foreach (var e in _tiles.Select(t => t.Card.Edition).Where(e => e.Length > 0))
                if (!editions.Contains(e))
                    editions.Add(e);
            EditionCombo.ItemsSource = editions;
            EditionCombo.SelectedItem = card.Edition.Length > 0 ? card.Edition : null;

            string cur = _data.CurrencySymbol;
            BoughtLabel.Text = cur.Length > 0 ? $"Bought for ({cur})" : "Bought for";
            BoughtBox.Text = card.BoughtFor?.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty;
            PulledCheck.IsChecked = card.Pulled;
            BoughtBox.IsEnabled = !card.Pulled;
            EditorMessage.Text = string.Empty;
        }

        private void SetEdition(string edition)
        {
            var items = EditionCombo.ItemsSource as List<string>;
            if (items != null && !items.Contains(edition))
            {
                items.Add(edition);
                EditionCombo.ItemsSource = items.ToList();
            }
            EditionCombo.SelectedItem = edition;
        }

        private async Task SaveAsync()
        {
            var tile = _selected;
            if (tile == null || _data == null || _path == null || _saving) return;

            string edition = EditionCombo.SelectedItem as string ?? string.Empty;
            string boughtText = (BoughtBox.Text ?? string.Empty).Trim();
            bool pulled = PulledCheck.IsChecked == true || CollectionReader.IsPulled(boughtText);
            decimal? bought = null;
            if (!pulled && boughtText.Length > 0)
            {
                bought = CollectionReader.ParseMoney(boughtText);
                if (bought == null)
                {
                    ShowEditorMessage("Bought for needs to be a number, like 50 or 49.99, or tick Pulled it myself.", error: true);
                    return;
                }
            }

            _saving = true;
            SaveButton.IsEnabled = false;
            ShowEditorMessage("Saving...", error: false);
            try
            {
                string path = _path;
                var card = tile.Card;
                int retention = AppConfig.Load().BackupRetentionCount;
                string cur = _data.CurrencySymbol;

                var newTile = await Task.Run(() =>
                {
                    var updated = CollectionWriter.Update(path, card, edition, bought, pulled, retention);
                    return new CardTile(updated, cur);
                });

                if (ReplaceTile(tile, newTile, path))
                    ShowEditorMessage("Saved to the spreadsheet.", error: false);
            }
            catch (Exception ex)
            {
                ShowEditorMessage(ex.Message, error: true);
            }
            finally
            {
                _saving = false;
                SaveButton.IsEnabled = true;
            }
        }

        private bool ReplaceTile(CardTile oldTile, CardTile newTile, string path)
        {
            int idx = _tiles.IndexOf(oldTile);
            if (idx < 0 || _path != path) return false;

            _tiles[idx] = newTile;
            oldTile.IsSelected = false;
            newTile.IsSelected = true;
            _selected = newTile;

            UpdateSummary();
            Refresh(resetScroll: false);
            FillEditor();
            return true;
        }

        private async Task AddImageAsync()
        {
            var tile = _selected;
            if (tile == null || _data == null || _path == null || _saving) return;

            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"Choose an image for {tile.Title}",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Images (*.jpg, *.png, *.webp, *.bmp, *.gif)")
                    {
                        Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.webp", "*.bmp", "*.gif" }
                    }
                }
            });
            if (files.Count == 0) return;

            _saving = true;
            SaveButton.IsEnabled = ImageButton.IsEnabled = false;
            ShowEditorMessage("Adding image...", error: false);
            try
            {
                string path = _path;
                string imagePath = files[0].Path.LocalPath;
                var card = tile.Card;
                int retention = AppConfig.Load().BackupRetentionCount;
                string cur = _data.CurrencySymbol;

                var newTile = await Task.Run(() =>
                {
                    byte[] image = PrepareImage(File.ReadAllBytes(imagePath));
                    var updated = CollectionWriter.SetImage(path, card, image, retention);
                    return new CardTile(updated, cur);
                });

                if (ReplaceTile(tile, newTile, path))
                    ShowEditorMessage("Image added to the spreadsheet.", error: false);
            }
            catch (Exception ex)
            {
                ShowEditorMessage(ex.Message, error: true);
            }
            finally
            {
                _saving = false;
                SaveButton.IsEnabled = ImageButton.IsEnabled = true;
            }
        }

        private static byte[] PrepareImage(byte[] data)
        {
            const int maxWidth = 600;
            const int maxBytes = 1_000_000;

            bool usable = ImageInfo.IsImage(data);
            bool tooBig = data.Length > maxBytes
                || (ImageInfo.TryGetSize(data, out int w, out _) && w > maxWidth);
            if (usable && !tooBig)
                return data;

            Bitmap bitmap;
            try
            {
                using (var probe = new MemoryStream(data))
                    bitmap = new Bitmap(probe);

                if (bitmap.PixelSize.Width > maxWidth)
                {
                    bitmap.Dispose();
                    using var ms = new MemoryStream(data);
                    bitmap = Bitmap.DecodeToWidth(ms, maxWidth, BitmapInterpolationMode.HighQuality);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("That file couldn't be read as an image.", ex);
            }

            using (bitmap)
            using (var output = new MemoryStream())
            {
                bitmap.Save(output, PngBitmapEncoderOptions.Default);
                return output.ToArray();
            }
        }

        private void ShowEditorMessage(string text, bool error)
        {
            EditorMessage.Text = text;
            EditorMessage.Foreground = new SolidColorBrush(Color.Parse(error ? "#F08080" : "#8FD18F"));
        }

        public async Task PickAndOpenAsync()
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select a spreadsheet to view",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Spreadsheets (*.xlsx, *.ods)") { Patterns = new[] { "*.xlsx", "*.ods" } },
                    new FilePickerFileType("Excel (*.xlsx)") { Patterns = new[] { "*.xlsx" } },
                    new FilePickerFileType("LibreOffice / OpenOffice (*.ods)") { Patterns = new[] { "*.ods" } }
                }
            });

            if (files.Count > 0)
                await LoadAsync(files[0].Path.LocalPath);
        }

        public async Task LoadAsync(string path)
        {
            OpenButton.IsEnabled = ReloadButton.IsEnabled = false;
            EmptyText.Text = "Loading...";
            EmptyText.IsVisible = true;
            CardList.ItemsSource = null;

            try
            {
                var (data, tiles) = await Task.Run(() =>
                {
                    var d = CollectionReader.Read(path);
                    return (d, d.Cards.Select(c => new CardTile(c, d.CurrencySymbol)).ToList());
                });

                _data = data;
                _tiles = tiles;
                _path = path;

                FileText.Text = Path.GetFileName(path) +
                        (data.SheetName.Length > 0 ? $" - {data.SheetName}" : "");
                ToolTip.SetTip(FileText, path);

                int? selectedRow = _selected?.Card.Row;
                _selected = null;

                PopulateGames();
                UpdateSummary();
                Refresh();

                Select(selectedRow is { } row ? _tiles.FirstOrDefault(t => t.Card.Row == row) : null);
            }
            catch (Exception ex)
            {
                _data = null;
                _tiles = new();
                Select(null);
                EmptyText.Text = $"Could not open {Path.GetFileName(path)}:{Environment.NewLine}{ex.Message}";
                EmptyText.IsVisible = true;
            }
            finally
            {
                OpenButton.IsEnabled = true;
                ReloadButton.IsEnabled = _path != null;
            }
        }

        private void PopulateGames()
        {
            var current = GameFilter.SelectedItem as string;
            var games = _tiles.Select(t => t.Card.Game)
                .Where(g => g.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var items = new List<string> { AllGames };
            items.AddRange(games);

            _suppressRefresh = true;
            GameFilter.ItemsSource = items;
            GameFilter.SelectedItem = current != null && items.Contains(current) ? current : AllGames;
            GameFilter.IsVisible = games.Count > 1;
            _suppressRefresh = false;
        }

        private void SetStatusFilter(bool owned, bool upgrade, bool notOwned)
        {
            _suppressRefresh = true;
            ShowOwned.IsChecked = owned;
            ShowUpgrade.IsChecked = upgrade;
            ShowNotOwned.IsChecked = notOwned;
            _suppressRefresh = false;
            Refresh();
        }

        private void Refresh() => Refresh(resetScroll: true);

        private void Refresh(bool resetScroll)
        {
            if (_suppressRefresh || _data == null)
                return;

            string search = (SearchBox.Text ?? string.Empty).Trim().ToLowerInvariant();
            string? game = GameFilter.SelectedItem as string;
            bool owned = ShowOwned.IsChecked == true;
            bool upgrade = ShowUpgrade.IsChecked == true;
            bool notOwned = ShowNotOwned.IsChecked == true;

            IEnumerable<CardTile> q = _tiles.Where(t => t.Card.Status switch
            {
                CardStatus.Owned => owned,
                CardStatus.NeedsUpgrade => upgrade,
                _ => notOwned
            });

            if (game != null && game != AllGames)
                q = q.Where(t => string.Equals(t.Card.Game, game, StringComparison.OrdinalIgnoreCase));

            if (search.Length > 0)
            {
                var words = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                q = q.Where(t => words.All(w => t.SearchText.Contains(w)));
            }

            var sort = SortCombo.SelectedItem as SortItem ?? _sorts[0];
            var shown = sort.Apply(q).ToList();

            var offset = CardScroller.Offset;
            CardList.ItemsSource = shown;
            if (resetScroll)
                CardScroller.Offset = default;
            else
                Avalonia.Threading.Dispatcher.UIThread.Post(() => CardScroller.Offset = offset,
                    Avalonia.Threading.DispatcherPriority.Loaded);

            string cur = _data.CurrencySymbol;
            ShowingText.Text = $"{shown.Count} of {_tiles.Count} cards";
            ShownValueText.Text = CardTile.Money(shown.Sum(t => t.Card.Price ?? 0m), cur);

            EmptyText.IsVisible = shown.Count == 0;
            EmptyText.Text = _tiles.Count == 0
                ? "No cards found in this spreadsheet."
                : "No cards match these filters.";
        }

        private void UpdateSummary()
        {
            if (_data == null) return;
            string cur = _data.CurrencySymbol;

            int ownedCount = _tiles.Count(t => t.Card.Status == CardStatus.Owned);
            int upgradeCount = _tiles.Count(t => t.Card.Status == CardStatus.NeedsUpgrade);
            int notOwnedCount = _tiles.Count(t => t.Card.Status == CardStatus.NotOwned);

            OwnedChipText.Text = $"Owned ({ownedCount})";
            UpgradeChipText.Text = $"Needs upgrade ({upgradeCount})";
            NotOwnedChipText.Text = $"Not owned ({notOwnedCount})";

            OwnedValueText.Text = CardTile.Money(
                _tiles.Where(t => t.Card.Status != CardStatus.NotOwned).Sum(t => t.Card.Price ?? 0m), cur);
            SpentText.Text = CardTile.Money(_tiles.Sum(t => t.Card.BoughtFor ?? 0m), cur);

            var pulled = _tiles.Where(t => t.Card.Pulled).ToList();
            PulledStat.IsVisible = pulled.Count > 0;
            PulledText.Text = $"{pulled.Count} worth {CardTile.Money(pulled.Sum(t => t.Card.Price ?? 0m), cur)}";
            ToCompleteText.Text = CardTile.Money(
                _tiles.Where(t => t.Card.Status == CardStatus.NotOwned).Sum(t => t.Card.Price ?? 0m), cur);
            PercentText.Text = _tiles.Count == 0
                ? "-"
                : $"{(double)(ownedCount + upgradeCount) / _tiles.Count:P0} ({ownedCount + upgradeCount}/{_tiles.Count})";
        }

        private static DateTime? ParseDate(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string[] formats = { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "MM/dd/yyyy", "M/d/yyyy", "dd.MM.yyyy", "yyyy" };
            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d)) return d;
            if (DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
            return null;
        }
    }
}
