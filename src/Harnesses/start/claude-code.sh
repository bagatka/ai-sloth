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

# Its memory goes to one place whatever folder it works in, so a host can keep it between nooks: user
# settings name it, keeping whatever else they hold. Repositories' settings can't move it.
settings="${HOME:-/root}/.claude/settings.json"
mkdir -p "$(dirname "$settings")"
node -e '
const fs = require("fs");
const file = process.argv[1];
let settings = {};
try { settings = JSON.parse(fs.readFileSync(file, "utf8")); } catch { }
settings.autoMemoryDirectory = "~/.claude/memory";
fs.writeFileSync(file, JSON.stringify(settings, null, 2) + "\n");
' "$settings"

exec claude-agent-acp
