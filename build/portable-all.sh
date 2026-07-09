#!/usr/bin/env bash
set -euo pipefail

version="${1:-}"
root="$(cd "$(dirname "$0")/.." && pwd)"

if [[ -z "$version" ]]; then
  version="$(dotnet msbuild "$root/src/Echo.App/Echo.App.csproj" -nologo -getProperty:Version | tr -d '[:space:]')"
fi

release_dir="$root/dist/releases"
mkdir -p "$release_dir"

publish_win_x64() {
  bash "$root/build/publish.sh" win-x64 "$version"

  local source_dir="$root/dist/win-x64"
  local zip_name="Echo-${version}-win-x64-portable.zip"
  local zip_path="$release_dir/$zip_name"
  local staging
  staging="$(mktemp -d)"

  if [[ ! -f "$source_dir/Echo.App.exe" ]]; then
    echo "Publish output missing: $source_dir/Echo.App.exe" >&2
    exit 1
  fi

  cp "$source_dir/Echo.App.exe" "$staging/"
  if [[ -d "$root/native/win-x64/directml" ]]; then
    cp -r "$root/native/win-x64/directml" "$staging/directml"
  fi

  rm -f "$zip_path"
  (cd "$staging" && zip -qr "$zip_path" .)
  rm -rf "$staging"

  local size_mb
  size_mb="$(awk "BEGIN {printf \"%.1f\", $(stat -c%s "$zip_path") / 1048576}")"
  echo "Portable: $zip_path ($size_mb MB)"
}

publish_win_x64
bash "$root/build/portable.sh" linux-x64 "$version"
bash "$root/build/portable.sh" osx-arm64 "$version"

echo "All portable archives in $release_dir"
