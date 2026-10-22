#!/bin/bash

# Fetch the latest release tag from GitHub API
LATEST_TAG=$(curl -s https://api.github.com/repos/ProfessorShroom/CardmarketPriceUpdater/releases/latest | grep '"tag_name":' | sed -E 's/.*"([^"]+)".*/\1/')

# Define directories
INSTALL_DIR="$HOME/.local/share/CardmarketPriceUpdater"
BIN_DIR="$HOME/.local/bin"
VERSION_FILE="$INSTALL_DIR/version.txt"

# Read local version or set to none
LOCAL_VERSION=$(cat "$VERSION_FILE" 2>/dev/null || echo "none")

if [ "$LATEST_TAG" = "$LOCAL_VERSION" ] && [ -f "$BIN_DIR/CardmarketPriceUpdater" ]; then
  echo "CardmarketPriceUpdater is already up to date ($LATEST_TAG)."
else
  echo "Updating CardmarketPriceUpdater to version $LATEST_TAG..."
  
  # Create required directories
  mkdir -p "$INSTALL_DIR"
  mkdir -p "$BIN_DIR"
  
  # Download to a temporary directory
  TMP_DIR=$(mktemp -d)
  curl -sL "https://github.com/ProfessorShroom/CardmarketPriceUpdater/releases/download/${LATEST_TAG}/CardmarketPriceUpdater-linux-x64.tar.gz" -o "$TMP_DIR/update.tar.gz"
  tar -xzf "$TMP_DIR/update.tar.gz" -C "$TMP_DIR"
  
  # Move and rename the binary directly into ~/.local/bin
  if [ -f "$TMP_DIR/Cardmarket-Price-Updater" ]; then
    mv "$TMP_DIR/Cardmarket-Price-Updater" "$BIN_DIR/CardmarketPriceUpdater"
  elif [ -f "$TMP_DIR/CardmarketPriceUpdater" ]; then
    mv "$TMP_DIR/CardmarketPriceUpdater" "$BIN_DIR/CardmarketPriceUpdater"
  fi
  
  # Ensure the binary is executable
  chmod +x "$BIN_DIR/CardmarketPriceUpdater"
  
  # Clean up temp files
  rm -rf "$TMP_DIR"
  
  # Save the new version tag
  echo "$LATEST_TAG" > "$VERSION_FILE"
  echo "Update complete! You can now run 'CardmarketPriceUpdater' from anywhere."
fi
