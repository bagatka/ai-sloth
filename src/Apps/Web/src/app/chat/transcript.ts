import { seconds } from "../api"

// A chat's transcript as rows to draw, folded from its events one at a time. Folding never changes a
// row it already made: it replaces it, so React redraws only rows that changed. Events follow the
// host's ChatEventBody (openapi.json); an agent's updates are Agent Client Protocol session updates
// (https://agentclientprotocol.com/protocol/prompt-turn), passed through unchanged.

export type ChatEvent = {
  sequence: number
  at: string
  kind: string
  // eslint-disable-next-line @typescript-eslint/no-explicit-any -- each kind has its own shape, read below
  event: any
}

export type MessageState = "waiting" | "answered" | "steered" | "cancelled"

export type ToolContent =
  | { type: "text"; text: string }
  | { type: "diff"; path: string; oldText: string | null; newText: string }

export type Row =
  | {
      kind: "message"
      key: string
      id: string
      sentBy: string
      text: string
      state: MessageState
      proposal: boolean
    }
  | { kind: "agent-text"; key: string; text: string }
  | { kind: "thought"; key: string; text: string }
  | {
      kind: "tool"
      key: string
      id: string
      title: string
      toolKind: string
      status: "pending" | "in_progress" | "completed" | "failed"
      content: ToolContent[]
      at: string
      endedAt: string | null
    }
  | {
      kind: "plan"
      key: string
      entries: { content: string; status: string }[]
    }
  | {
      kind: "turn-ended"
      key: string
      stopReason: string
      failure: string | null
      startedAt: string | null
      at: string
    }
  | {
      kind: "setup"
      key: string
      ended: boolean
      exitCode: number | null
      output: string | null
      /** How long it took, in seconds, once it ended. */
      took: number | null
    }
  | {
      kind: "notice"
      key: string
      tone: "info" | "warning"
      text: string
    }

export type Transcript = {
  rows: readonly Row[]
  /** Whether the agent is on a turn. */
  working: boolean
  /** When the running turn started. */
  turnStartedAt: string | null
  /** The last event folded in. */
  sequence: number
}

export const emptyTranscript: Transcript = {
  rows: [],
  working: false,
  turnStartedAt: null,
  sequence: 0,
}

/** The transcript with one more event folded in. */
export function fold(transcript: Transcript, chatEvent: ChatEvent): Transcript {
  if (chatEvent.sequence <= transcript.sequence) {
    return transcript
  }

  const next = { ...transcript, sequence: chatEvent.sequence }
  const body = chatEvent.event
  const key = String(chatEvent.sequence)
  switch (chatEvent.kind) {
    case "message-sent":
      return append(next, {
        kind: "message",
        key,
        id: body.messageId,
        sentBy: body.sentBy,
        text: body.text,
        state: "waiting",
        proposal: false,
      })
    case "message-proposed":
      return append(next, {
        kind: "message",
        key,
        id: body.messageId,
        sentBy: body.proposedBy,
        text: body.text,
        state: "waiting",
        proposal: true,
      })
    case "turn-started":
      return {
        ...setMessageState(next, body.messageId, "answered"),
        working: true,
        turnStartedAt: chatEvent.at,
      }
    case "message-steered":
      return setMessageState(next, body.messageId, "steered")
    case "message-cancelled":
      return setMessageState(next, body.messageId, "cancelled")
    case "agent-update":
      return foldUpdate(next, body.update ?? body, key, chatEvent.at)
    case "turn-ended": {
      const ended: Row = {
        kind: "turn-ended",
        key,
        stopReason: body.stopReason,
        failure: body.failure ?? null,
        startedAt: next.turnStartedAt,
        at: chatEvent.at,
      }
      return {
        ...next,
        rows: endToolCalls([...next.rows, ended], chatEvent.at),
        working: false,
        turnStartedAt: null,
      }
    }
    case "setup-started":
      return append(next, {
        kind: "setup",
        key,
        ended: false,
        exitCode: null,
        output: null,
        took: null,
      })
    case "setup-ended": {
      const index = findLast(next.rows, (row) => row.kind === "setup")
      const ended: Row = {
        kind: "setup",
        key: index >= 0 ? next.rows[index].key : key,
        ended: true,
        exitCode: body.exitCode,
        output: body.output ?? null,
        took: seconds(body.took),
      }
      return index >= 0 ? replace(next, index, ended) : append(next, ended)
    }
    case "agent-restarted":
      return append(next, {
        kind: "notice",
        key,
        tone: "info",
        text: body.remembers
          ? "The agent started again and remembers the conversation."
          : "The agent started again without its memory of this conversation.",
      })
    case "checkpoint-failed":
      return append(next, {
        kind: "notice",
        key,
        tone: "warning",
        text: "This turn's files weren't saved; the next turn tries again.",
      })
    case "disk-nearly-full":
      return append(next, {
        kind: "notice",
        key,
        tone: "warning",
        text: `The nook's disk is nearly full: ${gigabytes(body.usedBytes)} of ${gigabytes(body.totalBytes)} used.`,
      })
    default:
      // A checkpoint saved, or something newer than this app: nothing to draw.
      return next
  }
}

function foldUpdate(
  transcript: Transcript,
  // eslint-disable-next-line @typescript-eslint/no-explicit-any -- the agent's own update, read by its fields
  update: any,
  key: string,
  at: string
): Transcript {
  switch (update.sessionUpdate) {
    case "agent_message_chunk":
    case "agent_thought_chunk": {
      const kind =
        update.sessionUpdate === "agent_message_chunk"
          ? "agent-text"
          : "thought"
      const text = update.content?.type === "text" ? update.content.text : ""
      const last = transcript.rows.at(-1)
      if (last?.kind === kind) {
        return replace(transcript, transcript.rows.length - 1, {
          ...last,
          text: last.text + text,
        })
      }
      return text ? append(transcript, { kind, key, text }) : transcript
    }
    case "tool_call":
      return append(transcript, {
        kind: "tool",
        key,
        id: update.toolCallId,
        title: update.title ?? "",
        toolKind: update.kind ?? "other",
        status: update.status ?? "pending",
        content: toolContent(update.content),
        at,
        endedAt: null,
      })
    case "tool_call_update": {
      const index = findLast(
        transcript.rows,
        (row) => row.kind === "tool" && row.id === update.toolCallId
      )
      if (index < 0) {
        return transcript
      }
      const tool = transcript.rows[index] as Extract<Row, { kind: "tool" }>
      const status = update.status ?? tool.status
      return replace(transcript, index, {
        ...tool,
        title: update.title ?? tool.title,
        toolKind: update.kind ?? tool.toolKind,
        status,
        content: update.content ? toolContent(update.content) : tool.content,
        endedAt:
          status === "completed" || status === "failed" ? at : tool.endedAt,
      })
    }
    case "plan": {
      const entries = (update.entries ?? []).map(
        (entry: { content: string; status: string }) => ({
          content: entry.content,
          status: entry.status,
        })
      )
      const index = findLast(transcript.rows, (row) => row.kind === "plan")
      const turnEnded = findLast(
        transcript.rows,
        (row) => row.kind === "turn-ended"
      )
      // A turn keeps one plan, which the agent updates as it goes.
      return index > turnEnded
        ? replace(transcript, index, {
            kind: "plan",
            key: transcript.rows[index].key,
            entries,
          })
        : append(transcript, { kind: "plan", key, entries })
    }
    default:
      return transcript
  }
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any -- the agent's own content items
function toolContent(items: any[] | undefined): ToolContent[] {
  return (items ?? []).flatMap((item): ToolContent[] => {
    if (item.type === "diff") {
      return [
        {
          type: "diff",
          path: item.path,
          oldText: item.oldText ?? null,
          newText: item.newText ?? "",
        },
      ]
    }
    if (item.type === "content" && item.content?.type === "text") {
      return [{ type: "text", text: item.content.text }]
    }
    return []
  })
}

// A turn that ended ends its tool calls too: one still running then never finishes.
function endToolCalls(rows: Row[], at: string): Row[] {
  return rows.map((row) =>
    row.kind === "tool" &&
    (row.status === "pending" || row.status === "in_progress")
      ? { ...row, status: "failed", endedAt: at }
      : row
  )
}

function setMessageState(
  transcript: Transcript,
  messageId: string,
  state: MessageState
): Transcript {
  const index = findLast(
    transcript.rows,
    (row) => row.kind === "message" && row.id === messageId
  )
  if (index < 0) {
    return transcript
  }
  const message = transcript.rows[index] as Extract<Row, { kind: "message" }>
  return replace(transcript, index, { ...message, state })
}

function append(transcript: Transcript, row: Row): Transcript {
  return { ...transcript, rows: [...transcript.rows, row] }
}

function replace(transcript: Transcript, index: number, row: Row): Transcript {
  const rows = transcript.rows.slice()
  rows[index] = row
  return { ...transcript, rows }
}

function findLast(rows: readonly Row[], match: (row: Row) => boolean): number {
  for (let index = rows.length - 1; index >= 0; index--) {
    if (match(rows[index])) {
      return index
    }
  }
  return -1
}

function gigabytes(bytes: number): string {
  return `${(bytes / 1024 ** 3).toFixed(1)} GB`
}
