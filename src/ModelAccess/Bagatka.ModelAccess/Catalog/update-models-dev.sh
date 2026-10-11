#!/bin/sh
# Writes models-dev.json, the catalog a host starts from until it reads models.dev itself: the models
# of the vendors AiSloth's accounts reach, with what ModelCatalog reads of them. models.dev's data is
# MIT-licensed (models-dev.LICENSE). Run from the dev shell, which has node.
set -eu
cd "$(dirname "$0")"
curl -fsSL https://models.dev/api.json | node -e '
let text = "";
process.stdin.on("data", (chunk) => (text += chunk));
process.stdin.on("end", () => {
  const all = JSON.parse(text);
  const kept = {};
  for (const vendor of ["anthropic", "openai", "openrouter", "github-copilot"]) {
    const models = {};
    for (const [id, model] of Object.entries(all[vendor].models).sort(([a], [b]) => a.localeCompare(b))) {
      const { name, reasoning, reasoning_options, tool_call, limit, cost } = model;
      models[id] = { id, name, reasoning, reasoning_options, tool_call, limit, cost };
    }
    kept[vendor] = { models };
  }
  process.stdout.write(JSON.stringify(kept, null, 1) + "\n");
});
' > models-dev.json
