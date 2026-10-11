#!/bin/sh
# Starts Codex's ACP adapter from the variables every harness takes (src/Harnesses/README.md).
set -eu

case "$HARNESS_CREDENTIAL" in
  openai) ;;
  *) echo "Codex doesn't take $HARNESS_CREDENTIAL." >&2; exit 64 ;;
esac

# No sign-in through a browser. It acts without asking, since the nook is its sandbox; otherwise Codex
# also asks a second model to review every command it runs.
export NO_BROWSER=1 INITIAL_AGENT_MODE=agent-full-access

# The chat's model and how hard it reasons, merged into Codex's settings. Neither needs quoting: its
# host allows a model's ID letters, digits, and . _ - : / ~ @ + only, and reasoning low, medium,
# high, or xhigh.
CODEX_CONFIG=$(printf '{"model":"%s","model_reasoning_effort":"%s"}' "$HARNESS_MODEL" "$HARNESS_THINKING")
export CODEX_CONFIG

# The adapter signs Codex in to the gateway as its custom gateway when Codex needs a sign-in.
DEFAULT_AUTH_REQUEST=$(printf '{"methodId":"gateway","_meta":{"gateway":{"baseUrl":"%s","headers":{"Authorization":"Bearer %s"}}}}' "$HARNESS_MODEL_URL" "$HARNESS_TOKEN")
export DEFAULT_AUTH_REQUEST

exec codex-acp
