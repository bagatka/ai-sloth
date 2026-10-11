#!/bin/sh
# Starts pi's ACP adapter from the variables every harness takes (src/Harnesses/README.md). pi reads
# its providers from files, so this writes them: the gateway as the provider for the API, its default,
# the chat's model when one was chosen, and no startup banner in the agent's reply. The key stays a
# reference to the variable, which pi reads at each call. Copilot's API is a provider of its own,
# with the chat's model, the API Copilot takes for it, and whether it reasons.
set -eu

case "$HARNESS_CREDENTIAL" in
  openai | anthropic) provider="$HARNESS_CREDENTIAL" base="$HARNESS_MODEL_URL" ;;
  # OpenRouter's OpenAI chat API is at its /v1.
  openrouter) provider=openrouter base="$HARNESS_MODEL_URL/v1" ;;
  copilot) provider=copilot base="$HARNESS_MODEL_URL" ;;
  *) echo "pi doesn't take $HARNESS_CREDENTIAL." >&2; exit 64 ;;
esac

# No update checks or install telemetry from where agents run.
export PI_SKIP_VERSION_CHECK=1 PI_TELEMETRY=0

dir="${PI_CODING_AGENT_DIR:-${HOME:-/root}/.pi/agent}"
mkdir -p "$dir"
if [ "$provider" = copilot ]; then
  model=$(copilot-model)
  # shellcheck disable=SC2086 # its two words, split on purpose
  set -- $model
  reasoning=false
  if [ "$2" = reasons ]; then
    reasoning=true
  fi
  # shellcheck disable=SC2016 # pi expands $HARNESS_TOKEN itself, at each call.
  printf '{"providers":{"copilot":{"baseUrl":"%s","api":"openai-%s","apiKey":"$HARNESS_TOKEN","models":[{"id":"%s","reasoning":%s}]}}}\n' \
    "$base" "$1" "$HARNESS_MODEL" "$reasoning" > "$dir/models.json"
else
  # shellcheck disable=SC2016 # pi expands $HARNESS_TOKEN itself, at each call.
  printf '{"providers":{"%s":{"baseUrl":"%s","apiKey":"$HARNESS_TOKEN"}}}\n' "$provider" "$base" > "$dir/models.json"
fi

# The chat's model and thinking level. Neither needs quoting: its host allows a model's ID letters,
# digits, and . _ - : / ~ @ + only, and a thinking level one of pi's own.
printf '{"defaultProvider":"%s","defaultModel":"%s","defaultThinkingLevel":"%s","quietStartup":true}\n' \
  "$provider" "$HARNESS_MODEL" "$HARNESS_THINKING" > "$dir/settings.json"

exec pi-acp
