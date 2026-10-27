#!/bin/bash
# Readies a Claude Code on the web container to run the pre-commit gate: a .NET 10 SDK, the
# repository's hooks, and a warm package cache. Local sessions skip it.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

cd "$CLAUDE_PROJECT_DIR"

# The SDK global.json asks for, from Microsoft's installer. A distribution's package carries only the
# first feature band.
if [ -x "$HOME/.dotnet/dotnet" ]; then
  export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"
fi
if ! dotnet --version >/dev/null 2>&1; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --jsonfile global.json --install-dir "$HOME/.dotnet" >/dev/null
  export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"
fi
# The session's later commands and the pre-commit hook find the same SDK.
if [ -n "${CLAUDE_ENV_FILE:-}" ] && [ -x "$HOME/.dotnet/dotnet" ]; then
  echo "export DOTNET_ROOT=\"$HOME/.dotnet\" PATH=\"$HOME/.dotnet:\$PATH\"" >> "$CLAUDE_ENV_FILE"
fi

git config core.hooksPath .githooks
dotnet restore --locked-mode
