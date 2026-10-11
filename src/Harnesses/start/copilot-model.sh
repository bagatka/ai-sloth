#!/bin/sh
# What GitHub Copilot's API says of the chat's model, for the start scripts of harnesses on it: the
# API it takes, "responses" (OpenAI's Responses API) when it speaks that and "completions" (OpenAI's
# chat API) otherwise; and "reasons" when it takes a reasoning effort, "plain" otherwise. It asks
# Copilot's list of models through the model gateway, and fails when that can't be read, so an agent
# never starts on a guess.
set -eu

exec node -e '
const { HARNESS_MODEL_URL: gateway, HARNESS_TOKEN: token, HARNESS_MODEL: id } = process.env;
fetch(gateway + "/models", { headers: { authorization: "Bearer " + token } })
  .then(async (answer) => {
    if (!answer.ok) {
      throw new Error("Copilot answered " + answer.status + ": " + (await answer.text()).slice(0, 300));
    }
    const model = ((await answer.json()).data ?? []).find((found) => found.id === id);
    if (!model) {
      throw new Error("Copilot has no model " + id + ".");
    }
    const api = (model.supported_endpoints ?? []).includes("/responses") ? "responses" : "completions";
    const reasons = model.capabilities?.supports?.reasoning_effort ? "reasons" : "plain";
    console.log(api + " " + reasons);
  })
  .catch((error) => {
    console.error("Reading Copilot'"'"'s models failed: " + error.message);
    process.exit(1);
  });
'
