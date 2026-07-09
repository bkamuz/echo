#!/usr/bin/env bash
set -euo pipefail

rid="${1:?Usage: publish.sh <runtime-rid> [version]}"
version="${2:-}"

root="$(cd "$(dirname "$0")/.." && pwd)"
out_dir="$root/dist/$rid"
main_exe="Echo.App"

publish_args=(
  "$root/src/Echo.App/Echo.App.csproj"
  -c Release
  -r "$rid"
  --self-contained true
  -p:PublishSingleFile=true
  -p:IncludeNativeLibrariesForSelfExtract=true
  -p:EnableCompressionInSingleFile=true
  -p:DebugType=none
  -p:DebugSymbols=false
  -o "$out_dir"
)

if [[ -n "$version" ]]; then
  publish_args+=("-p:Version=$version")
fi

echo "Publishing Echo for $rid (single-file)..."
dotnet publish "${publish_args[@]}"

while IFS= read -r file; do
  [[ "$(basename "$file")" == "$main_exe" ]] || rm -f "$file"
done < <(find "$out_dir" -maxdepth 1 -type f)

echo "Output: $out_dir"
find "$out_dir" -type f | while read -r file; do
  rel="${file#"$out_dir"/}"
  size_mb="$(awk "BEGIN {printf \"%.1f\", $(stat -c%s "$file" 2>/dev/null || stat -f%z "$file") / 1048576}")"
  echo "  $rel ($size_mb MB)"
done
