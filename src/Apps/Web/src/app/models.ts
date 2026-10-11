// What a chat runs on, and how its parts read: plain values and words, shared by the pickers, the
// transcript, and the chat's header.

/**
 * What a chat runs on: its agent (harness), the account that pays, the model, and how hard it thinks;
 * the harness's own default for a model or thinking level of null.
 */
export type Choice = {
  harness: string
  account: string
  model: string | null
  thinking: string | null
}

const thinkingNames: Record<string, string> = {
  off: "Off",
  minimal: "Minimal",
  low: "Low",
  medium: "Medium",
  high: "High",
  xhigh: "Extra high",
  max: "Max",
}

/** A thinking level as people read it, such as "Extra high" for xhigh. */
export function thinkingLabel(level: string) {
  return thinkingNames[level] ?? level
}

/** A model as chats show it: its ID without the vendor, such as gpt-6-luna. */
export function modelLabel(model: string) {
  return model.slice(model.lastIndexOf("/") + 1)
}

/** What a chat runs as one line, such as "pi · gpt-6-luna · xhigh". */
export function agentLabel(
  harnessName: string,
  model: string | null,
  thinking: string | null = null
) {
  return [harnessName, model && modelLabel(model), thinking]
    .filter(Boolean)
    .join(" · ")
}
