#!/usr/bin/env bash
set -e

cd "$(dirname "$0")/.."

echo "Publishing linux-x64..."
rm -rf packaging/flatpak/publish-linux-x64
dotnet publish src/Avalonia/Cardmarket-Price-Updater.App.csproj \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -o packaging/flatpak/publish-linux-x64

echo "Building local flatpak..."
cd packaging/flatpak

# Clean cached build state
rm -rf .flatpak-builder builddir repo local-temp-manifest.yml

# Ensure temporary manifest is deleted on exit or failure
trap 'rm -f local-temp-manifest.yml' EXIT

# Create clean temporary manifest for local files
cp io.github.professorshroom.CardmarketPriceUpdater.yml local-temp-manifest.yml
sed -i '/^modules:/,$d' local-temp-manifest.yml

cat << 'EOF' >> local-temp-manifest.yml
modules:
  - name: cardmarket-price-updater
    buildsystem: simple
    build-commands:
      - install -Dm755 publish-linux-x64/Cardmarket-Price-Updater /app/bin/Cardmarket-Price-Updater
      - install -Dm644 io.github.professorshroom.CardmarketPriceUpdater.desktop /app/share/applications/io.github.professorshroom.CardmarketPriceUpdater.desktop
      - install -Dm644 io.github.professorshroom.CardmarketPriceUpdater.appdata.xml /app/share/metainfo/io.github.professorshroom.CardmarketPriceUpdater.appdata.xml
      - install -Dm644 app-icon-256.png /app/share/icons/hicolor/256x256/apps/io.github.professorshroom.CardmarketPriceUpdater.png
    sources:
      - type: dir
        path: publish-linux-x64
        dest: publish-linux-x64
      - type: file
        path: io.github.professorshroom.CardmarketPriceUpdater.desktop
      - type: file
        path: io.github.professorshroom.CardmarketPriceUpdater.appdata.xml
      - type: file
        path: app-icon-256.png
EOF

# Build, install, and export to repo in one step
flatpak-builder --user --install --force-clean --repo=repo builddir local-temp-manifest.yml

# Bundle into single file
flatpak build-bundle repo CardmarketPriceUpdater.flatpak io.github.professorshroom.CardmarketPriceUpdater

echo "Running..."
flatpak run io.github.professorshroom.CardmarketPriceUpdater
