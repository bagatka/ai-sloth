// What a chat runs on, and how its parts read: plain values and words, shared by the pickers, the
// transcript, and the chat's header.

/**
 * What a chat runs on: its agent (harness), the account that pays, the model, and how hard it thinks;
 * a model or thinking level of null isn't chosen yet. A new chat fills them in, in sight, before it
 * starts: the first model the account lists for the agent, and the agent's default thinking level.
 */
export type Choice = {
  harness: string
  account: string
  model: string | null
  thinking: string | null
}

/** A choice with nothing left open, as chats start and change on. */
export type Chosen = Choice & { model: string; thinking: string }

const thinkingNames: Record<string, string> = {
  none: "None",
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

/**
 * The thinking level a model runs at: the one chosen when the model takes it, the agent's default when
 * the model takes that, and the middle of the model's levels otherwise. A model that doesn't reason
 * (no levels) keeps the one chosen, which doesn't apply to it.
 */
export function fitThinking(
  chosen: string | null,
  levels: readonly string[],
  fallback: string
): string {
  if (chosen && (levels.length === 0 || levels.includes(chosen))) {
    return chosen
  }
  if (levels.length === 0 || levels.includes(fallback)) {
    return fallback
  }
  return levels[Math.floor((levels.length - 1) / 2)]
}

/** A count of tokens as people read it, such as 128k or 1M. */
export function tokensLabel(tokens: number): string {
  if (tokens >= 1_000_000) {
    return `${trimmed(tokens / 1_000_000)}M`
  }
  if (tokens >= 1_000) {
    return `${trimmed(tokens / 1_000)}k`
  }
  return String(tokens)
}

/** Dollars as people read them: $12.40, $0.84, $0.0031, with a leading ≈ for an estimate. */
export function dollarsLabel(dollars: number, estimate = false): string {
  const digits = dollars >= 1 || dollars === 0 ? 2 : dollars >= 0.01 ? 3 : 4
  const amount = `$${dollars.toFixed(digits)}`
  return estimate ? `≈ ${amount}` : amount
}

function trimmed(value: number): string {
  return value >= 100 ? value.toFixed(0) : value.toFixed(1).replace(/\.0$/, "")
}
