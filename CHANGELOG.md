# Changelog

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
