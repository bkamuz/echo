#!/usr/bin/env bash
set -euo pipefail

linux_common_init() {
  local script_dir
  script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  LINUX_BUILD_ROOT="$(cd "$script_dir/../.." && pwd)"
  LINUX_SOURCE_DIR="$LINUX_BUILD_ROOT/dist/linux-x64"
  LINUX_RELEASE_DIR="$LINUX_BUILD_ROOT/dist/releases"
  LINUX_ICON_SRC="$LINUX_BUILD_ROOT/src/Echo.App/Resources/main.png"
  LINUX_APP_ID="com.echo.App"
  LINUX_APP_NAME="Echo"
  LINUX_BINARY_NAME="Echo.App"

  mkdir -p "$LINUX_RELEASE_DIR"
}

linux_resolve_version() {
  local version="${1:-}"
  if [[ -z "$version" ]]; then
    version="$(dotnet msbuild "$LINUX_BUILD_ROOT/src/Echo.App/Echo.App.csproj" -nologo -getProperty:Version | tr -d '[:space:]')"
  fi
  printf '%s' "$version"
}

linux_ensure_publish() {
  local version
  version="$(linux_resolve_version "${1:-}")"

  if [[ ! -f "$LINUX_SOURCE_DIR/$LINUX_BINARY_NAME" ]]; then
    bash "$LINUX_BUILD_ROOT/build/publish.sh" linux-x64 "$version"
  fi

  chmod +x "$LINUX_SOURCE_DIR/$LINUX_BINARY_NAME"
}

linux_stage_icon() {
  local dest_dir="$1"
  mkdir -p "$dest_dir"
  cp "$LINUX_ICON_SRC" "$dest_dir/echo.png"
}

linux_write_desktop_file() {
  local dest="$1"
  local exec_path="$2"

  cat >"$dest" <<EOF
[Desktop Entry]
Type=Application
Name=${LINUX_APP_NAME}
Comment=Local speech-to-text dictation
Exec=${exec_path}
Icon=echo
Categories=AudioVideo;Audio;
StartupNotify=true
Terminal=false
EOF
}

linux_report_artifact() {
  local path="$1"
  local size_mb
  size_mb="$(awk "BEGIN {printf \"%.1f\", $(stat -c%s "$path" 2>/dev/null || stat -f%z "$path") / 1048576}")"
  echo "Linux package: $path ($size_mb MB)"
}
