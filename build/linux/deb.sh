#!/usr/bin/env bash
set -euo pipefail

version="${1:-}"

source "$(cd "$(dirname "$0")" && pwd)/common.sh"
linux_common_init
version="$(linux_resolve_version "$version")"
linux_ensure_publish "$version"

if ! command -v dpkg-deb >/dev/null 2>&1; then
  echo "dpkg-deb not found; skipping .deb package." >&2
  exit 0
fi

package_root="$(mktemp -d)"
trap 'rm -rf "$package_root"' EXIT

install -d "$package_root/opt/echo"
install -m755 "$LINUX_SOURCE_DIR/$LINUX_BINARY_NAME" "$package_root/opt/echo/$LINUX_BINARY_NAME"

install -d "$package_root/usr/share/applications"
linux_write_desktop_file "$package_root/usr/share/applications/${LINUX_APP_ID}.desktop" "/opt/echo/$LINUX_BINARY_NAME"

install -d "$package_root/usr/share/icons/hicolor/256x256/apps"
linux_stage_icon "$package_root/usr/share/icons/hicolor/256x256/apps"
mv "$package_root/usr/share/icons/hicolor/256x256/apps/echo.png" \
  "$package_root/usr/share/icons/hicolor/256x256/apps/${LINUX_APP_ID}.png"

install -d "$package_root/DEBIAN"
cat >"$package_root/DEBIAN/control" <<EOF
Package: echo
Version: ${version}
Section: sound
Priority: optional
Architecture: amd64
Depends: libicu74 | libicu72 | libicu70 | libicu67, libfontconfig1, libfreetype6, libharfbuzz0b, libx11-6, libice6, libsm6, libglib2.0-0, libdbus-1-3, libegl1, libgl1, alsa-utils, wl-clipboard | xclip, wtype | xdotool, python3-gi, gir1.2-atspi-2.0, util-linux-extra
Maintainer: Echo <noreply@example.com>
Homepage: https://github.com/bkamuz/echo
Description: Local speech-to-text dictation
 Echo converts speech to text locally with Whisper, GigaAM and Omnilingual models.
EOF

deb_path="$LINUX_RELEASE_DIR/Echo-${version}-linux-x64.deb"
rm -f "$deb_path"
dpkg-deb --build --root-owner-group "$package_root" "$deb_path"
linux_report_artifact "$deb_path"
