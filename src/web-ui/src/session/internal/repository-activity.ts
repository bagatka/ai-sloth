import type { SessionEvent } from ".."

export function repositoryActivity(events: readonly SessionEvent[]): {
  sequence: number | null
  updating: boolean
  reveal: boolean
} {
  const active = new Set<string>()
  let sequence: number | null = null
  let reveal = false

  for (const event of events) {
    if (event.type === "tool_started" && mutatesRepository(event.toolName)) {
      active.add(event.toolCallId)
      if (event.toolName === "edit" || event.toolName === "write") {
        reveal = true
      }
    } else if (
      event.type === "tool_finished" &&
      mutatesRepository(event.toolName)
    ) {
      active.delete(event.toolCallId)
      sequence = event.sequence
    }
  }

  return { sequence, updating: active.size > 0, reveal }
}

function mutatesRepository(toolName: string): boolean {
  return toolName === "bash" || toolName === "edit" || toolName === "write"
}
