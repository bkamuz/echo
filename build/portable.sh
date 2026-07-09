#!/usr/bin/env bash
set -euo pipefail

rid="${1:?Usage: portable.sh <runtime-rid> [version]}"

root="$(cd "$(dirname "$0")/.." && pwd)"
version="${2:-}"
if [[ -z "$version" ]]; then
  version="$(dotnet msbuild "$root/src/Echo.App/Echo.App.csproj" -nologo -getProperty:Version | tr -d '[:space:]')"
fi

bash "$root/build/publish.sh" "$rid" "$version"

source_dir="$root/dist/$rid"
release_dir="$root/dist/releases"
archive_name="Echo-${version}-${rid}-portable.tar.gz"
archive_path="$release_dir/$archive_name"
main_exe="$source_dir/Echo.App"

if [[ ! -f "$main_exe" ]]; then
  echo "Publish output missing: $main_exe" >&2
  exit 1
fi

mkdir -p "$release_dir"
rm -f "$archive_path"
tar -czf "$archive_path" -C "$source_dir" .

size_mb="$(awk "BEGIN {printf \"%.1f\", $(stat -c%s "$archive_path" 2>/dev/null || stat -f%z "$archive_path") / 1048576}")"
echo "Portable: $archive_path ($size_mb MB)"
