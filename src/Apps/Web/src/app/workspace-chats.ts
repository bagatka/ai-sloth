import { useSyncExternalStore } from "react"

import type { Schemas } from "./api"
import { apiElsewhere, apiUrl } from "./host"

// A workspace's chats as they change: the host's live list (GET /workspaces/{id}/chats/live). Each
// connection starts with the newest chats, then sends the ones that change, each replacing what the
// list had for it. The connection opens while someone looks at the list and closes a little after;
// the browser reconnects by itself, and the host starts over with the newest chats.

type ChatSummary = Schemas["ChatSummary"]

export type WorkspaceChats = {
  /** Newest first. */
  chats: readonly ChatSummary[]
  /** Whether the host sent the list yet. */
  loaded: boolean
}

// Coming back to the list shortly after leaving it finds it still connected.
const keepOpenFor = 30_000

class WorkspaceChatsStream {
  private readonly workspaceId: string
  private state: WorkspaceChats = { chats: [], loaded: false }
  private readonly listeners = new Set<() => void>()
  private source: EventSource | null = null
  private closeTimer = 0
  // Whether the next list is a connection's first, which replaces the whole list.
  private starting = true

  constructor(workspaceId: string) {
    this.workspaceId = workspaceId
  }

  subscribe = (listener: () => void) => {
    this.listeners.add(listener)
    window.clearTimeout(this.closeTimer)
    this.open()
    return () => {
      this.listeners.delete(listener)
      if (this.listeners.size === 0) {
        this.closeTimer = window.setTimeout(() => this.close(), keepOpenFor)
      }
    }
  }

  getSnapshot = () => this.state

  private open() {
    if (this.source) {
      return
    }

    const url = new URL(`workspaces/${this.workspaceId}/chats/live`, apiUrl)
    const source = new EventSource(url, { withCredentials: apiElsewhere })
    source.addEventListener("chats", (message) =>
      this.receive(
        JSON.parse((message as MessageEvent<string>).data) as ChatSummary[]
      )
    )
    source.onopen = () => {
      this.starting = true
    }
    // Closed means the host answered with something other than a stream, such as for a workspace
    // the person left; the list stays as it was. Otherwise the browser is already reconnecting.
    source.onerror = () => {
      if (source.readyState === EventSource.CLOSED) {
        this.source = null
      }
    }
    this.source = source
  }

  private close() {
    this.source?.close()
    this.source = null
  }

  private receive(changed: ChatSummary[]) {
    const byId = new Map(
      this.starting ? [] : this.state.chats.map((chat) => [chat.id, chat])
    )
    this.starting = false
    for (const chat of changed) {
      byId.set(chat.id, chat)
    }
    // IDs grow with time, and the host orders by them too.
    const chats = [...byId.values()].sort((left, right) =>
      left.id < right.id ? 1 : left.id > right.id ? -1 : 0
    )
    this.state = { chats, loaded: true }
    for (const listener of this.listeners) {
      listener()
    }
  }
}

const streams = new Map<string, WorkspaceChatsStream>()

/** A workspace's chats, live while the calling component shows them. */
export function useWorkspaceChats(
  workspaceId: string | undefined
): WorkspaceChats {
  const stream = workspaceId ? streamOf(workspaceId) : noWorkspace
  return useSyncExternalStore(stream.subscribe, stream.getSnapshot)
}

function streamOf(workspaceId: string): WorkspaceChatsStream {
  let stream = streams.get(workspaceId)
  if (!stream) {
    stream = new WorkspaceChatsStream(workspaceId)
    streams.set(workspaceId, stream)
  }
  return stream
}

const nothing: WorkspaceChats = { chats: [], loaded: false }
const noWorkspace = {
  subscribe: () => () => {},
  getSnapshot: () => nothing,
}
