#!/bin/sh
# Starts GitHub Copilot CLI in ACP mode from the variables every harness takes (src/Harnesses/README.md).
set -eu

case "$HARNESS_CREDENTIAL" in
  github-token) export COPILOT_GITHUB_TOKEN="$HARNESS_TOKEN" ;;
  *) echo "GitHub Copilot doesn't take $HARNESS_CREDENTIAL." >&2; exit 64 ;;
esac

exec copilot --acp
