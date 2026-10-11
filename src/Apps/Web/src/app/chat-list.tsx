import { useQuery } from "@tanstack/react-query"
import { Link, useParams } from "@tanstack/react-router"
import { cn } from "cn"

import { NookStateDot, type NookStateName } from "@/components/nook-state"
import { useSidebar } from "@/components/ui/sidebar"
import type { Schemas } from "./api"
import { dollarsLabel } from "./models"
import { chat as chatQuery, nooks, workspaces } from "./queries"
import { useChosenWorkspace } from "./workspace"
import { useWorkspaceChats } from "./workspace-chats"

// The person's chats in one workspace, grouped the way people look for them: the ones an agent is
// working on, then today's, then the rest, newest first in each.

export type ChatItem = {
  id: string
  title: string
  state: NookStateName
  when: string
  /** What it has cost so far, once that's a cent or more. */
  cost: string | null
  group: "Working" | "Today" | "Earlier"
}

/**
 * The workspace whose chats show: the open chat's, otherwise the one chosen last in this browser,
 * otherwise the person's first.
 */
export function useCurrentWorkspace(): Schemas["WorkspaceSummary"] | null {
  const all = useQuery(workspaces).data?.items ?? []
  const chosen = useChosenWorkspace()
  const { chatId } = useParams({ strict: false })
  const open = useQuery({ ...chatQuery(chatId ?? ""), enabled: !!chatId }).data
  return (
    all.find((workspace) => workspace.id === open?.workspaceId) ??
    all.find((workspace) => workspace.id === chosen) ??
    all[0] ??
    null
  )
}

export function useChatItems(workspaceId: string | undefined): {
  items: ChatItem[]
  loading: boolean
} {
  const list = useWorkspaceChats(workspaceId)
  const nookList = useQuery({
    ...nooks(workspaceId ?? ""),
    enabled: !!workspaceId,
  })
  const statuses = new Map(
    (nookList.data?.items ?? []).map((nook) => [nook.id, nook.status])
  )
  const startOfToday = new Date()
  startOfToday.setHours(0, 0, 0, 0)

  const items = list.chats.map((summary): ChatItem => {
    const working = summary.working
    const started = new Date(summary.startedAt)
    return {
      id: summary.id,
      title: summary.title ?? "New chat",
      state: working ? "working" : stateOf(statuses.get(summary.nookId)),
      when: ago(started),
      // Costs under a cent would only crowd the list.
      cost:
        summary.cost !== null && summary.cost >= 0.01
          ? dollarsLabel(summary.cost, summary.costEstimate)
          : null,
      group: working
        ? "Working"
        : started >= startOfToday
          ? "Today"
          : "Earlier",
    }
  })
  return { items, loading: !list.loaded }
}

export function ChatList({
  items,
  filter = "",
  className,
}: {
  items: ChatItem[]
  filter?: string
  className?: string
}) {
  const { chatId } = useParams({ strict: false })
  const sidebar = useSidebar()
  const shown = filter
    ? items.filter((item) =>
        item.title.toLowerCase().includes(filter.trim().toLowerCase())
      )
    : items
  const groups = (["Working", "Today", "Earlier"] as const)
    .map((name) => ({
      name,
      items: shown.filter((item) => item.group === name),
    }))
    .filter((group) => group.items.length > 0)

  if (filter && shown.length === 0) {
    return (
      <p className="px-4 py-3 text-label text-muted-foreground">
        No chat titles have “{filter.trim()}” in them.
      </p>
    )
  }

  return (
    <nav aria-label="Chats" className={cn("flex flex-col gap-3", className)}>
      {groups.map((group) => (
        <section key={group.name} className="flex flex-col gap-0.5 px-2">
          <h2 className="px-2 pt-1 pb-1.5 text-caption font-medium text-muted-foreground">
            {group.name}
          </h2>
          {group.items.map((item) => (
            <Link
              key={item.id}
              to="/chats/$chatId"
              params={{ chatId: item.id }}
              onClick={() => sidebar.setOpenMobile(false)}
              data-active={item.id === chatId || undefined}
              className="flex h-9 press items-center gap-2.5 rounded-lg px-2 text-sm text-sidebar-foreground focus-ring hover:bg-sidebar-accent data-active:bg-sidebar-accent data-active:font-medium"
            >
              <NookStateDot state={item.state} />
              <span className="min-w-0 flex-1 truncate">{item.title}</span>
              {item.cost && (
                <span className="text-xs text-muted-foreground tabular-nums">
                  {item.cost}
                </span>
              )}
              <span className="text-xs text-muted-foreground tabular-nums">
                {item.when}
              </span>
            </Link>
          ))}
        </section>
      ))}
    </nav>
  )
}

function stateOf(status: Schemas["NookStatus"] | undefined): NookStateName {
  switch (status) {
    case "Starting":
      return "starting"
    case "Asleep":
      return "asleep"
    case "Offline":
    case "Deleting":
      return "offline"
    case "Failed":
      return "failed"
    default:
      return "ready"
  }
}

// The interface's words are English, so its dates are too.
const weekday = new Intl.DateTimeFormat("en", { weekday: "short" })
const day = new Intl.DateTimeFormat("en", { month: "short", day: "numeric" })

function ago(at: Date): string {
  const minutes = Math.floor((Date.now() - at.getTime()) / 60_000)
  if (minutes < 1) return "now"
  if (minutes < 60) return `${minutes}m`
  if (minutes < 24 * 60) return `${Math.floor(minutes / 60)}h`
  if (minutes < 7 * 24 * 60) return weekday.format(at)
  return day.format(at)
}
