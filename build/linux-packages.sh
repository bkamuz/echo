#!/usr/bin/env bash
set -euo pipefail

version="${1:-}"
root="$(cd "$(dirname "$0")/.." && pwd)"

chmod +x "$root/build/linux/"*.sh

echo "Building Linux packages..."
bash "$root/build/linux/deb.sh" "$version" || true
bash "$root/build/linux/appimage.sh" "$version" || true
bash "$root/build/linux/flatpak.sh" "$version" || true

echo "Linux packages are in $root/dist/releases"
