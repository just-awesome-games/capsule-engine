#!/bin/bash
# Readies a Claude Code on the web container to run the pre-commit gate: a .NET 10 SDK, the
# repository's hooks, and a warm package cache. Local sessions skip it.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

cd "$CLAUDE_PROJECT_DIR"

if ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  apt-get update -qq || true
  DEBIAN_FRONTEND=noninteractive apt-get install -y -qq dotnet-sdk-10.0
fi

git config core.hooksPath .githooks
dotnet restore --locked-mode
