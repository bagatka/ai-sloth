#!/bin/sh
# Starts Claude Code's ACP adapter from the variables every harness takes (src/Harnesses/README.md).
set -eu

# No auto-updates, telemetry, or error reports from where agents run.
export CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1

case "$HARNESS_CREDENTIAL" in
  anthropic) export ANTHROPIC_BASE_URL="$HARNESS_MODEL_URL" ANTHROPIC_AUTH_TOKEN="$HARNESS_TOKEN" ;;
  claude-oauth-token) export CLAUDE_CODE_OAUTH_TOKEN="$HARNESS_TOKEN" ;;
  *) echo "Claude Code doesn't take $HARNESS_CREDENTIAL." >&2; exit 64 ;;
esac

exec claude-agent-acp
