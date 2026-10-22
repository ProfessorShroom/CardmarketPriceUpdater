#!/usr/bin/env bash
set -e

cd "$(dirname "$0")/.."

echo "Publishing linux-x64..."
rm -rf packaging/flatpak/publish-linux-x64
dotnet publish src/Avalonia/Cardmarket-Price-Updater.App.csproj \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -o packaging/flatpak/publish-linux-x64

echo "Building flatpak..."
flatpak remote-add --if-not-exists --user flathub https://dl.flathub.org/repo/flathub.flatpakrepo
flatpak install -y --user flathub org.flatpak.Builder

cd packaging/flatpak

# Clean cached layers so Flatpak rebuilds permissions properly
rm -rf .flatpak-builder builddir repo

flatpak uninstall -y --user io.github.professorshroom.CardmarketPriceUpdater 2>/dev/null || true
flatpak run --command=flathub-build org.flatpak.Builder --install io.github.professorshroom.CardmarketPriceUpdater.yml
flatpak build-bundle repo CardmarketPriceUpdater.flatpak io.github.professorshroom.CardmarketPriceUpdater

echo "Running..."
flatpak run io.github.professorshroom.CardmarketPriceUpdater
