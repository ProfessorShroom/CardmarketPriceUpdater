# Building & Packaging

## Windows

```
dotnet publish src/Avalonia/Cardmarket-Price-Updater.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
Output: `src/Avalonia/bin/Release/net8.0/win-x64/publish/Cardmarket-Price-Updater.exe`

Update checking no longer uses AutoUpdater.NET.Official - that package depends
on System.Windows.Forms, which forces the whole project onto the
`net8.0-windows` target framework and makes a Linux build impossible.
Instead, `Core/UpdateChecker.cs` is a small dependency-free class that reads
the same `update.xml` (now at the repo root) format over plain HTTP and
compares versions. The behaviour is different from before: it no longer
silently downloads and replaces the running executable, it just shows a
"new version available" link in the GUI that opens the download page. Bump
the `<version>`/`<url>` fields in `update.xml` on release the same way as
before.

## Linux (raw binary, no packaging)

```
dotnet publish src/Avalonia/Cardmarket-Price-Updater.App.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```
Output: `src/Avalonia/bin/Release/net8.0/linux-x64/publish/`

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
   flatpak-builder --user --install --force-clean build-dir \
     io.github.professorshroom.CardmarketPriceUpdater.yml
   ```

3. Run it:
   ```
   flatpak run io.github.professorshroom.CardmarketPriceUpdater
   ```

4. To distribute: either publish to Flathub (requires going through their
   review process and moving the manifest into their own repo), or host your
   own Flatpak repo and have users `flatpak remote-add` it - simplest for a
   small personal project is a single-file `.flatpak` bundle:
   ```
   flatpak build-bundle repo Cardmarket-Price-Updater.flatpak \
     io.github.professorshroom.CardmarketPriceUpdater
   ```
   which users install with `flatpak install Cardmarket-Price-Updater.flatpak`.

### Two manifests, two purposes

- `packaging/flatpak/io.github.professorshroom.CardmarketPriceUpdater.yml` - local testing. Points at a local folder (`type: dir`), which only works on your own machine after you've run `dotnet publish` yourself. Use this for the steps above.
- `packaging/flatpak/flathub/io.github.professorshroom.CardmarketPriceUpdater.yml` - the one to actually submit to Flathub. Flathub's build servers have no network access and don't know about your local folders, so this version pulls from a pinned, checksummed GitHub Release archive (`type: archive`) instead. This is the file to use for the initial Flathub submission PR (to `flathub/flathub`), and it's the one `.github/workflows/release-flatpak.yml` keeps updated afterward.

### Automating Flathub updates

`.github/workflows/release-flatpak.yml` runs on any tag matching `v*.*.*.*` (e.g. `v2.1.0.0`) and:

1. Publishes `linux-x64`, tars it up, and attaches it to a GitHub Release.
2. Computes its sha256.
3. Checks out `flathub/io.github.professorshroom.CardmarketPriceUpdater` (only exists after the one-time manual Flathub submission has been approved) and updates the manifest's archive URL/sha256 to point at the new release.
4. Commits and pushes - Flathub's own bot picks up that push and rebuilds.

Requires a `FLATHUB_TOKEN` repo secret: a GitHub personal access token with push access to the `flathub/io.github.professorshroom.CardmarketPriceUpdater` repo, added under Settings → Secrets and variables → Actions. That repo doesn't exist until Flathub approves the first submission, so this workflow won't do anything useful until then.

The Flatpak sandbox is given `--filesystem=home` (see the comment in the
manifest) so that the rotating `Backups/` folder can be created next to
whatever spreadsheet the user opens, anywhere in their home directory. This
is broader than the single-file access the GUI's file picker alone would
need. If that's not acceptable, the alternative is moving backups to
`$XDG_DATA_HOME/CardmarketPriceUpdater` instead of next to the source file -
let me know if you'd rather have that.
