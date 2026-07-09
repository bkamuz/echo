#!/usr/bin/env bash
set -euo pipefail

version="${1:-}"

source "$(cd "$(dirname "$0")" && pwd)/common.sh"
linux_common_init
version="$(linux_resolve_version "$version")"
linux_ensure_publish "$version"

tools_dir="$LINUX_BUILD_ROOT/dist/.linux-tools"
appimagetool="$tools_dir/appimagetool-x86_64.AppImage"
mkdir -p "$tools_dir"

if [[ ! -x "$appimagetool" ]]; then
  echo "Downloading appimagetool..."
  if ! wget -q -O "$appimagetool" \
    "https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage"; then
    echo "Failed to download appimagetool; skipping AppImage package." >&2
    exit 0
  fi
  chmod +x "$appimagetool"
fi

appdir="$(mktemp -d)"
trap 'rm -rf "$appdir"' EXIT

install -m755 "$LINUX_SOURCE_DIR/$LINUX_BINARY_NAME" "$appdir/$LINUX_BINARY_NAME"
linux_stage_icon "$appdir"

cat >"$appdir/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0" 2>/dev/null || realpath "$0")")"
exec "$HERE/Echo.App" "$@"
EOF
chmod +x "$appdir/AppRun"

linux_write_desktop_file "$appdir/${LINUX_APP_ID}.desktop" "Echo.App"
chmod 644 "$appdir/${LINUX_APP_ID}.desktop"

export ARCH=x86_64
export VERSION="$version"
appimage_path="$LINUX_RELEASE_DIR/Echo-${version}-linux-x64.AppImage"
rm -f "$appimage_path"

if [[ -n "${DISPLAY:-}" || -n "${WAYLAND_DISPLAY:-}" ]]; then
  "$appimagetool" "$appdir" "$appimage_path"
else
  echo "No display server detected; building AppImage in offline mode."
  "$appimagetool" --appimage-extract-and-run "$appdir" "$appimage_path"
fi

chmod +x "$appimage_path"
linux_report_artifact "$appimage_path"
