<p align="center">
  <img src="src/Avalonia/Assets/app-icon.png" alt="Computer Repair Centre Installer" height="300"/>
</p>

<p align="center">
  <a href="https://github.com/ProfessorShroom/CardmarketPriceUpdater/actions/workflows/build.yml"><img alt="Build" src="https://img.shields.io/github/actions/workflow/status/ProfessorShroom/CardmarketPriceUpdater/build.yml?style=for-the-badge&label=build"></a>
  <a href="https://github.com/ProfessorShroom/CardmarketPriceUpdater/releases"><img alt="Release" src="https://img.shields.io/github/v/release/ProfessorShroom/CardmarketPriceUpdater?style=for-the-badge&label=release&color=8A2BE2"></a> 
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge"> 
  <img alt="Platforrm" src="https://img.shields.io/badge/platform-Windows%2010%2F11%20x64%20%2F%20Linux%20(Flatpak)-0078D4?style=for-the-badge&logo=windows&logoColor=white"> 
  <a href="https://github.com/ProfessorShroom/CardmarketPriceUpdater/blob/main/LICENSE"><img alt="License" src="https://img.shields.io/badge/license-Apache-blue?style=for-the-badge"></a> 
</p>

## Cardmarket Price Updater

**_Copyright © Charlie Howard 2025-2026 All rights reserved_**

A C# based GUI/CLI that gets prices from [Cardmarket](https://www.cardmarket.com/en) based on spreadsheet contents for collection value purposes in GBP (£), EUR (€) or USD ($).

Currently supports Magic: The Gathering, Pokémon and Yu-Gi-Oh!

[Current Release Download](https://github.com/ProfessorShroom/Cardmarket-Price-Updater/releases)

At the moment, the cardmarket ID is manual, but when Cardmarket open up their API again, I will try to automate it.

The prices are based on an average of everything, so 1st Edition and Unlimited, etc. Once Cardmarket open up the API, I will be able to specify this to 1st Edition as this is what collectors want.

### Platforms

- **Windows** - Self-contained exe
- **Ubuntu / Fedora** - via Flatpak

### Installation & Updating (Linux via Flatpak)

To install or update to the latest release, run this command in your terminal:

```bash
curl -sL https://github.com/ProfessorShroom/CardmarketPriceUpdater/releases/latest/download/CardmarketPriceUpdater.flatpak -o CardmarketPriceUpdater.flatpak && flatpak install --user --assumeyes ./CardmarketPriceUpdater.flatpak && rm CardmarketPriceUpdater.flatpak
```

### Project layout

```
src/Core/       - shared logic: price lookup, FX conversion, retry/backoff, config, backups
src/Avalonia/   - the app itself (GUI + CLI entry point), one codebase for Windows and Linux
packaging/      - Flatpak manifest for Linux, update.xml for the Windows auto-updater
Template.xlsx   - starter spreadsheet
```

### Usage

Use the included Template.xlsx. The only 3 columns that are required are Card Price, Price Updated, and Cardmarket ID. The other columns are just there for you.

The template has 4 prefilled rows to show you how to fill it out.

Release Date isn't required, it's just for you. So you know when a card/set was released.

Game is so the program knows which card game to compare prices to, at the moment you can select MTG (Magic: The Gathering), Pokémon or Yu-Gi-Oh!

Set Name isn't required, but is just so you know what set that card is from, if, for example, the card has been released in multiple sets.

Set Code is only really used for Yu-Gi-Oh! from what I can tell, as that has a code on the card, eg, LOB-001, whereas others like MTG and Pokémon do not.

Card Price and Price Updated are filled out by the program, so leave them blank; they will get overwritten if you fill them out.

Cardmarket ID is the important part; this is the ID from Cardmarket's site that the program will use to find the correct card. For example, if you want to get the price of [LOB-001 Blue-Eyes White Dragon](https://www.cardmarket.com/en/YuGiOh/Products/Singles/Legend-of-Blue-Eyes-White-Dragon/Blue-Eyes-White-Dragon-V1-Ultra-Rare), the ID would be 577919. The easiest way to obtain this ID is to go to the URL of the card you want, right click on the card image and press either "Open Image in New Tab" or "Copy Image Link". That link will look like this [https://product-images.s3.cardmarket.com/5/LOB/577919/577919.jpg](https://product-images.s3.cardmarket.com/5/LOB/577919/577919.jpg), that 6 digit number before .jpg is our ID.

Another way to obtain the ID is by going to the card listing page and opening inspect (right click and press inspect, or press F12) and in that search box that opens, type in idProduct and hit enter, which will result in something like this

```json
<input type="hidden" name="idProduct" value="577919">
```

The value is what you put in Cardmarket ID.

Own? and Bought For are only required if you want to use the collection value part of the sheet, as this will add up the values of the cards you own and show you the amount left to spend. These values are an average so not 100% accurate but it's better than nothing.

### Backups

Every run copies your spreadsheet into a `Backups` folder next to it before making any changes, named `<filename>.<timestamp>.bak`. By default the 5 most recent backups per file are kept and older ones are pruned automatically; change this in the config file (see below) if you want more or fewer.

### Retries

Downloads (price guide + FX rate) automatically retry a few times with a short backoff if Cardmarket or the FX API has a hiccup, instead of failing the whole run on one bad request. Retry count and delay are configurable (see below).

### Config file

Your last-used currency and price type are remembered automatically. Retry and backup settings live in a small JSON config file:

- Windows: `%AppData%\CardmarketPriceUpdater\config.json`
- Linux: `~/.config/CardmarketPriceUpdater/config.json` (or the Flatpak-sandboxed equivalent)

```json
{
  "CurrencyMode": "AUTO",
  "PriceType": "avg30",
  "MaxRetries": 3,
  "RetryDelaySeconds": 2,
  "BackupRetentionCount": 5
}
```

It's created automatically on first run with these defaults - delete it to reset.

### CLI Usage

Run the executable from a terminal using these commands for headless use (same on Windows and Linux):

- /f to specify a filename, for example: `./Cardmarket-Price-Updater /f ./Template.xlsx`
- /d to specify a directory, this will run through every .xlsx in that directory, for example: `./Cardmarket-Price-Updater /d ./`
- /r combined with /d, also searches subdirectories
- /c to specify a currency, by default it's set to auto which will use GBP, for example: `./Cardmarket-Price-Updater /c e /f ./Template.xlsx` (`/c e` is EUR €, `/c p` is GBP £, `/c u` is USD $)
- /log to log output to a file, for example: `./Cardmarket-Price-Updater /f ./Template.xlsx /log ./log.txt`
- /q, /quiet, /s, /silent all allow you to run silently with no console output at all, for example `./Cardmarket-Price-Updater /f ./Template.xlsx /s`

### Example Spreadsheet

| Release Date | Game      | Set Name                           | Card Name              | Set Code   | Card Price (£) | Price Updated | Cardmarket ID | Rarity                | Own? | Edition | Bought For (£) | Collection Value (£)              | Remaning Cards | Total Cards |
| ------------ | --------- | ---------------------------------- | ---------------------- | ---------- | -------------- | ------------- | ------------- | --------------------- | ---- | ------- | -------------- | --------------------------------- | -------------- | ----------- |
| 12/09/2025   | Yu-Gi-Oh! | Nike Collaboration Cards (special) | Red-Eyes Black Dragon  | NKC1-EN002 | £402.53        | 2026-08-26    | 845882        | Prismatic Secret Rare | ✔    | LIMITED | £200.00        | £28,799.95                        | 1              | 4           |
| 08/03/2002   | Yu-Gi-Oh! | Legend of Blue-Eyes White Dragon   | Blue-Eyes White Dragon | LOB-001    | £467.11        | 2026-08-26    | 577919        | Ultra Rare            | ✖    | ✖       |                | Amount Spent (£)                  |                |             |
| 05/08/1993   | MTG       | Alpha                              | Black Lotus            |            | £9,876.27      | 2026-08-26    | 5465          | Rare                  | ✔    | ✔       | £20,000.00     | £22,250.00                        |                |             |
| 09/01/1999   | Pokémon   | Base Set                           | Charizard              | BS 4       | £2,119.74      | 2026-08-26    | 660224        | Holo Rare             | ✔    | PSA9    | £2,000.00      | Amount to Complete Collection (£) |                |             |
| 18/11/2008   | Yu-Gi-Oh! | Crossroads of Chaos                | Black Rose Dragon      | CSOC-EN039 | £245.40        | 2026-08-26    | 108490        | Ghost Rare            | ✔    | PSA10   | £50.00         | £116.06                           |                |             |

#### Changelog

#### Latest Update

**Update 2.0.2.0**

- Removed Flathub compliance, it's too strict and not worth the hassle.

#### Older Updates

**Update 2.0.1.0**

- Flathub compliance fixes for the Flatpak packaging (metadata/manifest cleanup) - no functional changes to the app itself.

**Update 2.0.0.0**

- Rewrote the GUI in Avalonia instead of WinForms, so it now runs on Linux (Ubuntu/Fedora, packaged as a Flatpak) as well as Windows, from one shared codebase.
- Added USD ($) as a third currency alongside GBP and EUR.
- Downloads now retry automatically with backoff instead of failing the run on one bad request.
- Backups are now timestamped and kept in a `Backups` folder with automatic pruning, instead of a single overwritten `.bak` file.
- Added a small JSON config file for default currency/price type and retry/backup settings, created automatically on first run.
- Replaced AutoUpdater.NET.Official (which required WinForms and blocked a Linux build entirely) with a small built-in update checker on Windows - it now shows a "new version available" link instead of silently self-updating.

**Update 1.4.0.0**

- Changed quiet mode to actually hide the CLI completely.
- Added auto update feature.

**Update 1.3.0.0**

- Updated GUI to a more modern look.
- Added support to select pricing model; Trending Price, 7-Day Average Price and 30-Day Average Price. By default, it is set to 30-Day Average Price, but you can change it to Trending or 7-Day Average Price if you want a more stable price.

**Update 1.2.0.0**

- Added cmd/terminal support.
- /f lets you specify a file.
- /d lets you specify a directory.
- /c lets you specify a currency.
- /log lets you log to a file.
- /q, /quiet, /s, /silent runs the exe silently.

**Update 1.1.2.0**

- Updated EUR to GBP conversion link.

**Update 1.1.1.0**

- Moved Version/Readme link to [professorshroom.com](https://professorshroom.com)

**Update 1.1.0.0**

- Added Game to spreadsheet to specify the card game.
- Will now check prices against the correct game instead of checking all.
