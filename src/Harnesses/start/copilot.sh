#!/bin/sh
# Starts GitHub Copilot CLI in ACP mode from the variables every harness takes (src/Harnesses/README.md),
# with the model gateway as its model provider: Copilot's API, which the gateway pays with the plan's
# token, so no GitHub token reaches the nook.
set -eu

case "$HARNESS_CREDENTIAL" in
  copilot) ;;
  *) echo "GitHub Copilot doesn't take $HARNESS_CREDENTIAL." >&2; exit 64 ;;
esac

# The API Copilot takes for the chat's model, and whether the model reasons.
model=$(copilot-model)
# shellcheck disable=SC2086 # its two words, split on purpose
set -- $model
export COPILOT_PROVIDER_BASE_URL="$HARNESS_MODEL_URL" COPILOT_PROVIDER_BEARER_TOKEN="$HARNESS_TOKEN" COPILOT_PROVIDER_WIRE_API="$1"

# The chat's model, and how hard it reasons when it can: none, minimal, low, medium, high, xhigh, or max.
if [ "$2" = reasons ]; then
  exec copilot --acp --model "$HARNESS_MODEL" --reasoning-effort "$HARNESS_THINKING"
fi
exec copilot --acp --model "$HARNESS_MODEL"
