#!/bin/bash

APP_ID="io.github.professorshroom.CardmarketPriceUpdater"
REPO="ProfessorShroom/CardmarketPriceUpdater"
INSTALL_DIR="$HOME/.local/share/CardmarketPriceUpdater"
VERSION_FILE="$INSTALL_DIR/flatpak-version.txt"

# Fetch the latest release tag from GitHub API
LATEST_TAG=$(curl -s "https://api.github.com/repos/$REPO/releases/latest" | grep '"tag_name":' | sed -E 's/.*"([^"]+)".*/\1/')

if [ -z "$LATEST_TAG" ]; then
  echo "Could not determine the latest version (network issue or GitHub rate limit)." >&2
  exit 1
fi

# Read local version or set to none
LOCAL_VERSION=$(cat "$VERSION_FILE" 2>/dev/null || echo "none")

if [ "$LATEST_TAG" = "$LOCAL_VERSION" ] && flatpak info --user "$APP_ID" >/dev/null 2>&1; then
  echo "CardmarketPriceUpdater is already up to date ($LATEST_TAG)."
  exit 0
fi

echo "Updating CardmarketPriceUpdater to version $LATEST_TAG..."
mkdir -p "$INSTALL_DIR"

# Download to a temporary directory, cleaned up on exit
TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT

if ! curl -fsL "https://github.com/$REPO/releases/download/${LATEST_TAG}/CardmarketPriceUpdater.flatpak" -o "$TMP_DIR/CardmarketPriceUpdater.flatpak"; then
  echo "Download failed." >&2
  exit 1
fi

OUTPUT=$(flatpak install --user --assumeyes --or-update "$TMP_DIR/CardmarketPriceUpdater.flatpak" 2>&1)
STATUS=$?
echo "$OUTPUT"

# Treat "already installed" as success (e.g. first run before a version file exists)
if [ $STATUS -eq 0 ] || echo "$OUTPUT" | grep -q "is already installed"; then
  echo "$LATEST_TAG" > "$VERSION_FILE"
  echo "Update complete! Run it with: flatpak run $APP_ID"
else
  echo "Install failed." >&2
  exit 1
fi
