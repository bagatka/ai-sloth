import { useSyncExternalStore } from "react"

import { apiElsewhere, apiUrl } from "../host"
import {
  emptyTranscript,
  fold,
  type ChatEvent,
  type Transcript,
} from "./transcript"

// A chat as it happens: the host's event stream for it (GET /chats/{id}/events), folded into its
// transcript. The connection opens when someone looks at the chat and closes a little after the last
// one stops; the browser reconnects by itself after a dropped connection, resuming after the last
// event it got. Events arrive in bursts while an agent streams, so they're folded once per frame.

export type PendingMessage = {
  /** This browser's name for it until the host names it. */
  key: string
  text: string
  /** The host's ID once it took the message. */
  id: string | null
}

export type ChatState = {
  transcript: Transcript
  /** Messages this browser sent that the host hasn't reported yet. */
  pending: readonly PendingMessage[]
  /** Whether events arrive as they happen; false while connecting or reconnecting. */
  live: boolean
  /** Whether the host refused the stream: the chat is gone or the browser is signed out. */
  refused: boolean
}

// Every kind of event the host sends (ChatEventBody in its openapi.json).
const kinds = [
  "message-sent",
  "message-proposed",
  "turn-started",
  "message-steered",
  "message-cancelled",
  "agent-update",
  "turn-ended",
  "checkpoint-saved",
  "checkpoint-failed",
  "agent-restarted",
  "setup-started",
  "setup-ended",
  "disk-nearly-full",
] as const

// Going back to a chat shortly after leaving it finds it still connected.
const keepOpenFor = 30_000

class ChatStream {
  readonly chatId: string
  private state: ChatState = {
    transcript: emptyTranscript,
    pending: [],
    live: false,
    refused: false,
  }
  private readonly listeners = new Set<() => void>()
  private source: EventSource | null = null
  private queue: ChatEvent[] = []
  private frame = 0
  private closeTimer = 0
  private nextKey = 0
  lastUsed = 0

  constructor(chatId: string) {
    this.chatId = chatId
  }

  subscribe = (listener: () => void) => {
    this.listeners.add(listener)
    window.clearTimeout(this.closeTimer)
    this.open()
    return () => {
      this.listeners.delete(listener)
      this.lastUsed = performance.now()
      if (this.listeners.size === 0) {
        this.closeTimer = window.setTimeout(() => this.close(), keepOpenFor)
      }
    }
  }

  getSnapshot = () => this.state

  get watched() {
    return this.listeners.size > 0
  }

  /** Follows the chat again after the host refused it, such as after signing in again. */
  retry() {
    this.close()
    this.open()
  }

  /** Shows a message as sent at once, before the host reports it. */
  addPending(text: string): string {
    const key = `pending-${this.nextKey++}`
    this.set({ pending: [...this.state.pending, { key, text, id: null }] })
    return key
  }

  /** The host took the message: it stays pending until its own event arrives. */
  taken(key: string, id: string) {
    const known = this.state.transcript.rows.some(
      (row) => row.kind === "message" && row.id === id
    )
    this.set({
      pending: known
        ? this.state.pending.filter((message) => message.key !== key)
        : this.state.pending.map((message) =>
            message.key === key ? { ...message, id } : message
          ),
    })
  }

  /** The host refused the message: it was never sent. */
  dropPending(key: string) {
    this.set({
      pending: this.state.pending.filter((message) => message.key !== key),
    })
  }

  private open() {
    if (this.source) {
      return
    }

    const url = new URL(`chats/${this.chatId}/events`, apiUrl)
    url.searchParams.set("after", String(this.state.transcript.sequence))
    const source = new EventSource(url, { withCredentials: apiElsewhere })
    for (const kind of kinds) {
      source.addEventListener(kind, (message) =>
        this.receive(kind, (message as MessageEvent<string>).data)
      )
    }
    source.onopen = () => this.set({ live: true, refused: false })
    source.onerror = () => {
      // Closed means the host answered with something other than a stream; otherwise the browser
      // is already reconnecting.
      if (source.readyState === EventSource.CLOSED) {
        this.source = null
        this.set({ live: false, refused: true })
      } else {
        this.set({ live: false })
      }
    }
    this.source = source
  }

  private close() {
    this.source?.close()
    this.source = null
    window.cancelAnimationFrame(this.frame)
    this.frame = 0
    this.flush()
    this.set({ live: false })
  }

  private receive(kind: string, data: string) {
    const parsed = JSON.parse(data) as {
      sequence: number
      at: string
      event: unknown
    }
    this.queue.push({
      sequence: parsed.sequence,
      at: parsed.at,
      kind,
      event: parsed.event,
    })
    if (!this.frame) {
      this.frame = window.requestAnimationFrame(() => {
        this.frame = 0
        this.flush()
      })
    }
  }

  private flush() {
    if (this.queue.length === 0) {
      return
    }

    let transcript = this.state.transcript
    for (const chatEvent of this.queue) {
      transcript = fold(transcript, chatEvent)
    }
    this.queue = []
    const reported = new Set(
      transcript.rows.flatMap((row) => (row.kind === "message" ? [row.id] : []))
    )
    this.set({
      transcript,
      pending: this.state.pending.filter(
        (message) => !message.id || !reported.has(message.id)
      ),
    })
  }

  private set(change: Partial<ChatState>) {
    this.state = { ...this.state, ...change }
    for (const listener of this.listeners) {
      listener()
    }
  }
}

// The chats this page follows, and a few it followed lately, so going back to one shows it at once
// and asks the host only for what happened since.
const streams = new Map<string, ChatStream>()
const kept = 8

/** The stream of one chat; looking at it (useChat) is what connects it. */
export function chatStream(chatId: string): ChatStream {
  let stream = streams.get(chatId)
  if (!stream) {
    stream = new ChatStream(chatId)
    streams.set(chatId, stream)
    const idle = [...streams.values()]
      .filter((other) => !other.watched && other !== stream)
      .sort((left, right) => left.lastUsed - right.lastUsed)
    for (const old of idle.slice(0, Math.max(0, streams.size - kept))) {
      streams.delete(old.chatId)
    }
  }
  return stream
}

/** A chat as it happens, live while the calling component shows it. */
export function useChat(chatId: string): ChatState {
  const stream = chatStream(chatId)
  return useSyncExternalStore(stream.subscribe, stream.getSnapshot)
}
