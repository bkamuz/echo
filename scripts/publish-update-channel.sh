#!/usr/bin/env bash
set -euo pipefail

# Update latest.json on the source repository (same repo as the GitHub Release).
# Does not mirror release assets to a separate updates repo.

if [[ -z "${VERSION:-}" ]]; then
  echo "VERSION is required" >&2
  exit 1
fi

if [[ -z "${GH_TOKEN:-}" ]]; then
  echo "GH_TOKEN is required" >&2
  exit 1
fi

REPO="${UPDATES_REPO:-${GITHUB_REPOSITORY:-bkamuz/echo}}"
TAG="v${VERSION}"
ASSET_NAME="Echo-${VERSION}-win-x64-portable.zip"
DOWNLOAD_URL="https://github.com/${REPO}/releases/download/${TAG}/${ASSET_NAME}"
RELEASE_NOTES_URL="https://github.com/${REPO}/releases/tag/${TAG}"

export GH_TOKEN

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

git clone --depth 1 "https://x-access-token:${GH_TOKEN}@github.com/${REPO}.git" "$WORK_DIR/repo"
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

echo "Published update manifest for ${TAG} to ${REPO}"
