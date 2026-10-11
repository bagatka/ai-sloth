#!/bin/sh
# Starts Claude Code's ACP adapter from the variables every harness takes (src/Harnesses/README.md).
set -eu

# No auto-updates, telemetry, or error reports from where agents run.
export CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1

case "$HARNESS_CREDENTIAL" in
  anthropic) export ANTHROPIC_BASE_URL="$HARNESS_MODEL_URL" ANTHROPIC_AUTH_TOKEN="$HARNESS_TOKEN" ;;
  openrouter)
    # OpenRouter speaks Anthropic's Messages API at its root, under its own names for models: Claude
    # Code's aliases, and the small model of its side calls, go to OpenRouter's newest Claude models.
    export ANTHROPIC_BASE_URL="$HARNESS_MODEL_URL" ANTHROPIC_AUTH_TOKEN="$HARNESS_TOKEN" \
      ANTHROPIC_DEFAULT_OPUS_MODEL="~anthropic/claude-opus-latest" \
      ANTHROPIC_DEFAULT_SONNET_MODEL="~anthropic/claude-sonnet-latest" \
      ANTHROPIC_DEFAULT_HAIKU_MODEL="~anthropic/claude-haiku-latest"
    ;;
  claude-oauth-token) export CLAUDE_CODE_OAUTH_TOKEN="$HARNESS_TOKEN" ;;
  *) echo "Claude Code doesn't take $HARNESS_CREDENTIAL." >&2; exit 64 ;;
esac

# The chat's model does its main work whichever alias Claude Code starts on; side calls keep the
# small model. It thinks as hard as chosen: low, medium, high, xhigh, or max.
export ANTHROPIC_MODEL="$HARNESS_MODEL" ANTHROPIC_DEFAULT_OPUS_MODEL="$HARNESS_MODEL" ANTHROPIC_DEFAULT_SONNET_MODEL="$HARNESS_MODEL"
export CLAUDE_CODE_EFFORT_LEVEL="$HARNESS_THINKING"

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
