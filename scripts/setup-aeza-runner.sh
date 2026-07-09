#!/usr/bin/env bash
set -euo pipefail

REG_TOKEN="${1:?Usage: setup-runner.sh <registration-token>}"

echo "=== Installing dependencies ==="
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq zip unzip curl ca-certificates libicu-dev dpkg-dev wget

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Installing .NET 10 SDK..."
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet
  ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet
fi
echo "dotnet: $(dotnet --version)"
zip -v | head -1

RUNNER_VER="2.322.0"
RUNNER_DIR="/opt/actions-runner"

if [[ -f "$RUNNER_DIR/.runner" ]]; then
  echo "Runner already configured. Restarting service..."
  systemctl restart "actions.runner.bkamuz-echo.aeza-personal.service" 2>/dev/null || \
    (cd "$RUNNER_DIR" && ./svc.sh start)
  exit 0
fi

echo "=== Installing GitHub Actions runner v$RUNNER_VER ==="
export RUNNER_ALLOW_RUNASROOT=1
mkdir -p "$RUNNER_DIR"
cd "$RUNNER_DIR"
curl -fsSL -o actions-runner.tar.gz "https://github.com/actions/runner/releases/download/v${RUNNER_VER}/actions-runner-linux-x64-${RUNNER_VER}.tar.gz"
tar xzf actions-runner.tar.gz
rm actions-runner.tar.gz

./config.sh \
  --url "https://github.com/bkamuz/echo" \
  --token "$REG_TOKEN" \
  --name "aeza-personal" \
  --labels "self-hosted,Linux,aeza-personal" \
  --unattended \
  --replace

./svc.sh install root
./svc.sh start

echo "=== Runner installed ==="
systemctl status "actions.runner.bkamuz-echo.aeza-personal.service" --no-pager || ./svc.sh status
