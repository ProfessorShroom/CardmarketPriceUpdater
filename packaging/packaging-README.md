# Building & Packaging

## Windows

```
dotnet publish src/Avalonia/Cardmarket-Price-Updater.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
Output: `src/Avalonia/bin/Release/net10.0/win-x64/publish/Cardmarket-Price-Updater.exe`

Update checking no longer uses AutoUpdater.NET.Official - that package depends
on System.Windows.Forms, which forces the whole project onto the
`net10.0-windows` target framework and makes a Linux build impossible.
Instead, `Core/UpdateChecker.cs` is a small dependency-free class that reads
the same `update.xml` (now at the repo root) format over plain HTTP and
compares versions. The behaviour is different from before: it no longer
silently downloads and replaces the running executable, it just shows a
"new version available" link in the GUI that opens the download page. `release-windows.yml` sets the version in `update.xml` on `main` once
the new exe has been uploaded, so it does not need to be bumped by hand.

## Linux (raw binary, no packaging)

```
dotnet publish src/Avalonia/Cardmarket-Price-Updater.App.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```
Output: `src/Avalonia/bin/Release/net10.0/linux-x64/publish/`

This runs directly on Ubuntu, Fedora, or any other x86_64 distro with the usual GUI libraries present (X11 or Wayland, plus the standard font/graphics stack most desktops already have). No .NET install required - it's self-contained.

## Linux (Flatpak - recommended distribution method)

Flatpak is what covers Ubuntu *and* Fedora from a single artifact, and gives you `flatpak update` for free instead of needing your own updater on Linux.

1. Publish the self-contained Linux build into the folder the manifest expects:
   ```
   dotnet publish src/Avalonia/Cardmarket-Price-Updater.App.csproj -c Release -r linux-x64 \
     --self-contained true -p:PublishSingleFile=true \
     -o packaging/flatpak/publish-linux-x64
   ```

2. Build and install locally to test:
   ```
   cd packaging/flatpak
   flatpak-builder --user --install --force-clean --repo=repo builddir \
     io.github.professorshroom.CardmarketPriceUpdater.yml
   ```

3. Run it:
   ```
   flatpak run io.github.professorshroom.CardmarketPriceUpdater
   ```

4. To distribute, bundle it into a single `.flatpak` file:
   ```
   flatpak build-bundle repo Cardmarket-Price-Updater.flatpak \
     io.github.professorshroom.CardmarketPriceUpdater
   ```
   which users install with `flatpak install Cardmarket-Price-Updater.flatpak`.

### Automated releases

`.github/workflows/release-flatpak.yml` runs on any tag matching `v*.*.*.*` (e.g. `v3.0.0.0`). It publishes `linux-x64`, builds the Flatpak from that binary and attaches `CardmarketPriceUpdater.flatpak` to the GitHub Release. No extra secrets are needed.

The workflow adds the Flathub remote only to install the Freedesktop runtime and SDK; the app itself is not published there.

The same workflow adds the release to the appdata from `CHANGELOG.md` (using `scripts/appdata_release.py`) before building. `update-readme-changelog.yml` keeps the committed appdata up to date whenever `CHANGELOG.md` is pushed to `main`.

### Sandbox permissions

The Flatpak is given `--filesystem=home` so that the rotating `Backups/` folder
can be created next to whatever spreadsheet the user opens, anywhere in their
home directory. This is broader than the single-file access the file picker
alone would need.
