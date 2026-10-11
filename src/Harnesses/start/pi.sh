#!/bin/sh
# Starts pi's ACP adapter from the variables every harness takes (src/Harnesses/README.md). pi reads
# its providers from files, so this writes them: the gateway as the provider for the API, its default,
# the chat's model when one was chosen, and no startup banner in the agent's reply. The key stays a
# reference to the variable, which pi reads at each call. A Copilot plan's token goes to pi's own
# GitHub Copilot provider instead, which reads it from COPILOT_GITHUB_TOKEN.
set -eu

case "$HARNESS_CREDENTIAL" in
  openai | anthropic) provider="$HARNESS_CREDENTIAL" base="$HARNESS_MODEL_URL" ;;
  # OpenRouter's OpenAI chat API is at its /v1.
  openrouter) provider=openrouter base="$HARNESS_MODEL_URL/v1" ;;
  github-token) provider=github-copilot base="" ;;
  *) echo "pi doesn't take $HARNESS_CREDENTIAL." >&2; exit 64 ;;
esac

# No update checks or install telemetry from where agents run.
export PI_SKIP_VERSION_CHECK=1 PI_TELEMETRY=0

dir="${PI_CODING_AGENT_DIR:-${HOME:-/root}/.pi/agent}"
mkdir -p "$dir"
if [ -n "$base" ]; then
  # shellcheck disable=SC2016 # pi expands $HARNESS_TOKEN itself, at each call.
  printf '{"providers":{"%s":{"baseUrl":"%s","apiKey":"$HARNESS_TOKEN"}}}\n' "$provider" "$base" > "$dir/models.json"
else
  export COPILOT_GITHUB_TOKEN="$HARNESS_TOKEN"
  rm -f "$dir/models.json"
fi

# The chat's model and thinking level. Neither needs quoting: its host allows a model's ID letters,
# digits, and . _ - : / ~ @ + only, and a thinking level one of pi's own.
printf '{"defaultProvider":"%s","defaultModel":"%s","defaultThinkingLevel":"%s","quietStartup":true}\n' \
  "$provider" "$HARNESS_MODEL" "$HARNESS_THINKING" > "$dir/settings.json"

exec pi-acp
