import { useSyncExternalStore } from "react"

import type { Schemas } from "./api"
import { apiUrl } from "./host"
import { LiveEvents } from "./live-events"

// A workspace's chats as they change: the host's live list (GET /workspaces/{id}/chats/live). Each
// connection starts with the newest chats, then sends the ones that change, each replacing what the
// list had for it. The connection opens while someone looks at the list and closes a little after;
// it reconnects after a dropped connection or a restart of the host (LiveEvents), and the host starts
// over with the newest chats.

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
  private readonly events: LiveEvents
  private closeTimer = 0
  // Whether the next list is a connection's first, which replaces the whole list.
  private starting = true

  constructor(workspaceId: string) {
    this.workspaceId = workspaceId
    this.events = new LiveEvents(
      () => new URL(`workspaces/${this.workspaceId}/chats/live`, apiUrl),
      (source) =>
        source.addEventListener("chats", (message) =>
          this.receive(
            JSON.parse((message as MessageEvent<string>).data) as ChatSummary[]
          )
        ),
      (live) => {
        if (live) {
          this.starting = true
        }
      }
    )
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
    this.events.open()
  }

  private close() {
    this.events.close()
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
    for (const listener of changeListeners) {
      listener(this.workspaceId)
    }
  }
}

const streams = new Map<string, WorkspaceChatsStream>()

// Who hears of each change to a workspace's chats, such as the cache of its nooks, whose states
// move when its chats do: a turn starts or ends, or a new chat's nook finished starting.
const changeListeners = new Set<(workspaceId: string) => void>()

/** Calls `listener` with the workspace each time some of its chats change. */
export function onChatsChange(listener: (workspaceId: string) => void) {
  changeListeners.add(listener)
}

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
