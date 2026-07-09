#!/usr/bin/env bash
set -euo pipefail

version="${1:-}"

source "$(cd "$(dirname "$0")" && pwd)/common.sh"
linux_common_init
version="$(linux_resolve_version "$version")"
linux_ensure_publish "$version"

if ! command -v flatpak-builder >/dev/null 2>&1; then
  echo "flatpak-builder not found; skipping Flatpak package." >&2
  exit 0
fi

flatpak remote-add --if-not-exists --user flathub https://flathub.org/repo/flathub.flatpakrepo >/dev/null 2>&1 || true

staging_dir="$(mktemp -d)"
trap 'rm -rf "$staging_dir"' EXIT

install -m755 "$LINUX_SOURCE_DIR/$LINUX_BINARY_NAME" "$staging_dir/$LINUX_BINARY_NAME"
linux_stage_icon "$staging_dir"
linux_write_desktop_file "$staging_dir/${LINUX_APP_ID}.desktop" "Echo.App"
cp "$LINUX_BUILD_ROOT/build/flatpak/${LINUX_APP_ID}.yml" "$staging_dir/"

build_dir="$(mktemp -d)"
repo_dir="$LINUX_BUILD_ROOT/dist/.flatpak-repo"
mkdir -p "$repo_dir"

flatpak-builder \
  --force-clean \
  --user \
  --repo="$repo_dir" \
  "$build_dir" \
  "$staging_dir/${LINUX_APP_ID}.yml"

flatpak_path="$LINUX_RELEASE_DIR/Echo-${version}-linux-x64.flatpak"
rm -f "$flatpak_path"
flatpak build-bundle "$repo_dir" "$flatpak_path" "$LINUX_APP_ID" "$version"
linux_report_artifact "$flatpak_path"
