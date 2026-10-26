#!/bin/bash
# Cloud sessions start without the .NET SDK. This installs it from Ubuntu's archive (the installer at dot.net is
# blocked there) and restores the solution's packages, so a session can build and test straight away.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

if ! command -v dotnet >/dev/null 2>&1; then
  sudo=""
  [ "$(id -u)" = "0" ] || sudo="sudo"
  $sudo apt-get update -qq >/dev/null
  DEBIAN_FRONTEND=noninteractive $sudo apt-get install -y -qq dotnet-sdk-10.0 >/dev/null
fi

cd "$CLAUDE_PROJECT_DIR"
dotnet restore ElyntOptimizer.slnx --verbosity quiet >/dev/null
echo "The .NET SDK $(dotnet --version) is installed and the solution's packages are restored."
