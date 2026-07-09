#!/usr/bin/env bash
set -euo pipefail

rid="${1:?Usage: publish.sh <runtime-rid> [version]}"
version="${2:-}"

root="$(cd "$(dirname "$0")/.." && pwd)"
out_dir="$root/dist/$rid"
main_exe="Echo.App"
if [[ "$rid" == win-* ]]; then
  main_exe="Echo.App.exe"
fi

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

prune_loose_artifacts() {
  case "$rid" in
    linux-*)
      rm -rf "$out_dir/directml" "$out_dir/runtimes/macos-arm64" "$out_dir/runtimes/macos-x64"
      rm -rf "$out_dir/runtimes/win-arm64" "$out_dir/runtimes/win-x64" "$out_dir/runtimes/win-x86"
      rm -f "$out_dir/ggml-metal.metal"
      ;;
    osx-*)
      rm -rf "$out_dir/directml"
      rm -rf "$out_dir/runtimes/linux-x64" "$out_dir/runtimes/linux-arm64"
      rm -rf "$out_dir/runtimes/win-arm64" "$out_dir/runtimes/win-x64" "$out_dir/runtimes/win-x86"
      ;;
    win-*)
      rm -rf "$out_dir/runtimes/linux-x64" "$out_dir/runtimes/linux-arm64"
      rm -rf "$out_dir/runtimes/macos-arm64" "$out_dir/runtimes/macos-x64"
      rm -f "$out_dir/ggml-metal.metal"
      ;;
  esac

  rmdir "$out_dir/runtimes" 2>/dev/null || true
}

prune_loose_artifacts

while IFS= read -r file; do
  [[ "$(basename "$file")" == "$main_exe" ]] || rm -f "$file"
done < <(find "$out_dir" -maxdepth 1 -type f)

if [[ "$rid" != win-* && -f "$out_dir/$main_exe" ]]; then
  chmod +x "$out_dir/$main_exe"
fi

echo "Output: $out_dir"
find "$out_dir" -type f | while read -r file; do
  rel="${file#"$out_dir"/}"
  size_mb="$(awk "BEGIN {printf \"%.1f\", $(stat -c%s "$file" 2>/dev/null || stat -f%z "$file") / 1048576}")"
  echo "  $rel ($size_mb MB)"
done
