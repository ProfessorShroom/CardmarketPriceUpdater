# Changelog

## 4.0.0.0 - The Collection Update

### Added
- Added a Collection tab that shows every card in a spreadsheet as a tile with its image, set, rarity, price, edition and what you paid for it.
- Cards can be shown or hidden by status (Owned, Needs upgrade, Not owned), with quick Only upgrades / Only not owned / Show all buttons.
- Added search by name, set name, set code, rarity or Cardmarket ID, a game filter, and sorting by price, release date, set or status.
- Added a summary bar with the value of the cards shown, collection value, amount spent, cost to complete and the percentage owned.
- Cards can be edited from the Collection tab: click a card to set its Edition and Bought For, then save straight back to the spreadsheet. A backup is made before the first change to each file, and saving is refused while the file is open in LibreOffice or Excel.
- Added an Add image button to the card editor for cards whose image couldn't be downloaded. Pick a JPG, PNG, WebP, BMP or GIF and it is placed in the card's Image cell, replacing any existing picture, and saved to the image cache. Large images are shrunk automatically.
- Cards you pulled yourself can be marked as Pulled in the card editor. This saves "Pulled" in Bought For instead of a price, counts as nothing spent, and the summary bar shows how many cards you pulled and what they're worth.
- Edition badges use the same colours as the template: PSA10 is purple with gold, PSA9 is purple, upgrades are blue, owned is green and not owned is red.

### Changed
- The main window now has two tabs, Update Prices and Collection, and opens larger.
- After a price update the Collection tab loads the updated spreadsheet so the new prices show straight away.

### Fixed
- Totals such as Collection Value and Amount Spent are now correct when the spreadsheet is opened after an update, instead of showing old values until it is recalculated.
- The template's Bought For colouring now shows Pulled in green instead of red.
- Log lines in the app and the CLI no longer show the time twice.
- The template's Total Cards now counts every card rather than only owned ones, so % of Cards Owned is correct.

## 3.0.0.0 - The .NET 10 Update

### Changed
- Upgraded from .NET 8 to .NET 10.
- Upgraded Avalonia from 11.2.1 to 12.2.0.
- Upgraded EPPlus from 8.6.1 to 8.7.3.
- Changed the license from Apache 2.0 to a Personal Use License.
- The Flatpak release information and `update.xml` now update automatically on release.
- `update.xml` is only updated once the new Windows exe is available to download.

### Fixed
- Fixed known security vulnerabilities in packages used by EPPlus (`System.Security.Cryptography.Xml` and `System.Security.Cryptography.Pkcs`).
- Corrected the license, links and release information in the Flatpak metadata.

### Removed
- Removed the leftover Flathub packaging (`nuget-sources.json`, the Flathub manifest and its docs). The Flatpak download on the releases page is unaffected.

## 2.3.0.2

### Fixed
- Corrected how images are download, set codes aren't always consistent. This will require more updates as I go and find additional set codes that don't work.

## 2.3.0.1

### Fixed
- Widened window as the close button was cut off.
- Stopped Cardmarket from rejecting image download requests.

## 2.3.0.0

### Added
- Added an image field to the spreadsheet (this is optional, but a nice touch), the software can download an image of the card from Cardmarket and please at in the first column.

## 2.2.0.1

### Fixed
- Corrected some comments and dates.

## 2.2.0.0

### Added
- Added support for LibreOffice / OpenOffice spreadsheets (`.ods`) alongside `.xlsx`. Prices and the Price Updated date are written straight into the `.ods` file, and the rest of the file (styles, column widths, currency formatting, other sheets) is left untouched. No LibreOffice install is needed.

### Changed
- The GUI file picker now offers `.xlsx` and `.ods` (with an "all spreadsheets" filter), and the CLI `/d` flag now picks up `.ods` files as well as `.xlsx` ones.
- CLI `/d` now matches `.xlsx`/`.ods` case-insensitively on Linux and skips Excel's temporary `~$` lock files.

### Fixed
- An empty sheet no longer crashes the program when it looks for the column headers.

## 2.1.0.0

### Added
- Added a game dropdown to the GUI and a `/g` flag to the CLI (`/g y` Yu-Gi-Oh!, `/g m` Magic: The Gathering, `/g p` Pokémon) to choose the default game. The choice is remembered as `defaultGame` in the config file.

### Changed
- The `Game` column is now optional. If a sheet has no Game column, every row uses the default game, which is Yu-Gi-Oh! unless you choose another.
- Prices are now checked against the correct game for each row instead of all three games at once, and only the price guides for games actually in your sheet are downloaded. Blank or unrecognised Game cells fall back to the default game.

### Fixed
- Fixed a temp file being left behind when the required column headers could not be found.
- Fixed the README config example using the wrong (PascalCase) key names.

## 2.0.3.2

### Fixed
- Fixed Flatpak crash due to missing argument in dotnot build command.

## 2.0.3.1

### Changed
- Moved `update.xml` back to the repo root (pre-2.0 installs expect it there) and updated the update-checker URL, docs, and project layout references to match.

### Fixed
- Fixed the Linux CLI release workflow failing with a 403 - it was missing the `contents: write` permission the other two release workflows already had, so it couldn't create/attach to the release.
- Fixed a few leftover references to the old hyphenated repo name (`Cardmarket-Price-Updater`) in the README and update-checker URL, pointing them at the current `CardmarketPriceUpdater`.

## 2.0.3.0

### Added
- Added a self-contained Linux `linux-x64` CLI build, published alongside the exe and Flatpak on every release - lets headless/server users run the CLI directly with no Flatpak, GUI runtime, or display required.

### Changed
- Releases are now fully automated: pushing a version tag (`vx.x.x.x`) builds and attaches the Windows exe, the Flatpak bundle, and the Linux CLI tarball to the release, no manual upload needed.

### Fixed
- Fixed `packaging/flatpak/io.github.professorshroom.CardmarketPriceUpdater.yml` - it was still cloning an old hyphenated repo name pinned to `v2.0.2.0` instead of building from your local checkout. It now builds from a local publish output, matching what the packaging docs always said it did.

## 2.0.2.0

### Removed
- Removed Flathub compliance, it's too strict and not worth the hassle.

## 2.0.1.0

### Fixed
- Flathub compliance fixes for the Flatpak packaging (metadata/manifest cleanup) - no functional changes to the app itself.

## 2.0.0.0 - The .NET 8 / Avalonia Update

### Added
- Added USD ($) as a third currency alongside GBP and EUR.
- Added a small JSON config file for default currency/price type and retry/backup settings, created automatically on first run.

### Changed
- Rewrote the GUI in Avalonia instead of WinForms, so it now runs on Linux (Ubuntu/Fedora, packaged as a Flatpak) as well as Windows, from one shared codebase.
- Downloads now retry automatically with backoff instead of failing the run on one bad request.
- Backups are now timestamped and kept in a `Backups` folder with automatic pruning, instead of a single overwritten `.bak` file.
- Replaced AutoUpdater.NET.Official (which required WinForms and blocked a Linux build entirely) with a small built-in update checker on Windows - it now shows a "new version available" link instead of silently self-updating.

## 1.4.0.0

### Added
- Added auto update feature.

### Changed
- Changed quiet mode to actually hide the CLI completely.

## 1.3.0.0

### Added
- Added support to select pricing model; Trending Price, 7-Day Average Price and 30-Day Average Price. By default, it is set to 30-Day Average Price, but you can change it to Trending or 7-Day Average Price if you want a more stable price.

### Changed
- Updated GUI to a more modern look.

## 1.2.0.0

### Added
- Added cmd/terminal support.
- /f lets you specify a file.
- /d lets you specify a directory.
- /c lets you specify a currency.
- /log lets you log to a file.
- /q, /quiet, /s, /silent runs the exe silently.

## 1.1.2.0

### Changed
- Updated EUR to GBP conversion link.

## 1.1.1.0

### Changed
- Moved Version/Readme link to [professorshroom.com](https://professorshroom.com)

## 1.1.0.0

### Added
- Added Game to spreadsheet to specify the card game.

### Changed
- Will now check prices against the correct game instead of checking all.
