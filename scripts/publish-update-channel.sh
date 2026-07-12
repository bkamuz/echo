#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${VERSION:-}" ]]; then
  echo "VERSION is required" >&2
  exit 1
fi

if [[ -z "${GH_TOKEN:-}" ]]; then
  echo "GH_TOKEN is required" >&2
  exit 1
fi

UPDATES_REPO="${UPDATES_REPO:-bkamuz/echo-updates}"
ZIP_PATH="${1:-}"
if [[ -z "$ZIP_PATH" || ! -f "$ZIP_PATH" ]]; then
  echo "Portable zip path is required as the first argument" >&2
  exit 1
fi

TAG="v${VERSION}"
ASSET_NAME="Echo-${VERSION}-win-x64-portable.zip"
DOWNLOAD_URL="https://github.com/${UPDATES_REPO}/releases/download/${TAG}/${ASSET_NAME}"
RELEASE_NOTES_URL="https://github.com/${UPDATES_REPO}/releases/tag/${TAG}"

export GH_TOKEN

if gh release view "$TAG" --repo "$UPDATES_REPO" >/dev/null 2>&1; then
  gh release upload "$TAG" "$ZIP_PATH#${ASSET_NAME}" --repo "$UPDATES_REPO" --clobber
else
  gh release create "$TAG" \
    --repo "$UPDATES_REPO" \
    --title "Echo ${VERSION}" \
    --notes "Windows portable update for Echo ${VERSION}." \
    "$ZIP_PATH#${ASSET_NAME}"
fi

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

git clone --depth 1 "https://x-access-token:${GH_TOKEN}@github.com/${UPDATES_REPO}.git" "$WORK_DIR/repo"
cd "$WORK_DIR/repo"

cat > latest.json <<EOF
{
  "version": "${VERSION}",
  "downloadUrl": "${DOWNLOAD_URL}",
  "releaseNotesUrl": "${RELEASE_NOTES_URL}"
}
EOF

git config user.name "github-actions[bot]"
git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
git add latest.json
git diff --staged --quiet && echo "latest.json unchanged" || {
  git commit -m "Update latest.json for ${TAG}"
  git push origin HEAD:main
}

echo "Published update channel for ${TAG} to ${UPDATES_REPO}"
