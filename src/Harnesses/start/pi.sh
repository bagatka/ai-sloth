#!/bin/sh
# Starts pi's ACP adapter from the variables every harness takes (src/Harnesses/README.md). pi reads
# its providers from files, so this writes them: the gateway as the provider for the API, its default,
# and no startup banner in the agent's reply. The key stays a reference to the variable, which pi
# reads at each call.
set -eu

case "$HARNESS_CREDENTIAL" in
  openai | anthropic) provider="$HARNESS_CREDENTIAL" ;;
  *) echo "pi doesn't take $HARNESS_CREDENTIAL." >&2; exit 64 ;;
esac

# No update checks or install telemetry from where agents run.
export PI_SKIP_VERSION_CHECK=1 PI_TELEMETRY=0

dir="${PI_CODING_AGENT_DIR:-${HOME:-/root}/.pi/agent}"
mkdir -p "$dir"
printf '{"providers":{"%s":{"baseUrl":"%s","apiKey":"$HARNESS_TOKEN"}}}\n' "$provider" "$HARNESS_MODEL_URL" > "$dir/models.json"
printf '{"defaultProvider":"%s","quietStartup":true}\n' "$provider" > "$dir/settings.json"

exec pi-acp
