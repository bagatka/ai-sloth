import { lazy, Suspense, useMemo, useState } from "react"
import {
  BotIcon,
  ExternalLinkIcon,
  FilesIcon,
  GitPullRequestIcon,
  TerminalIcon,
  UserIcon,
} from "lucide-react"
import { Button } from "@/components/ui/button"
import type { AuthenticatedRequest, SessionEvent, SessionTurn } from ".."
import { isUnifiedPatch } from "./patch"
import { SessionDiffPanel } from "./session-diff-panel"
import { useSession } from "./use-session"

const InlineDiff = lazy(() =>
  import("./inline-diff").then((module) => ({ default: module.InlineDiff }))
)

export function SessionWorkspace({
  request,
  workspaceId,
  sessionId,
}: {
  request: AuthenticatedRequest
  workspaceId: string
  sessionId: string
}) {
  const session = useSession(request, workspaceId, sessionId)
  const [diffOpen, setDiffOpen] = useState(false)
  const running =
    session.details?.turns.at(-1)?.status === "running" ||
    session.details?.turns.at(-1)?.status === "finalizing"
  const publication = session.details?.publication ?? null
  const publicationIsCurrent =
    publication !== null && publication.revision === session.details?.revision

  if (session.status === "loading" && !session.details) {
    return <CenteredMessage>Loading session…</CenteredMessage>
  }
  if (session.status === "error" && !session.details) {
    return (
      <CenteredMessage>
        <p className="mb-3 text-destructive">{session.error}</p>
        <Button variant="outline" onClick={session.reload}>
          Try again
        </Button>
      </CenteredMessage>
    )
  }
  if (!session.details) return null

  return (
    <div className="relative flex min-h-0 flex-1">
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="shrink-0 border-b bg-background px-6 py-3">
          <div className="mx-auto flex max-w-4xl items-center gap-3">
            <div className="min-w-0 flex-1">
              <h1 className="truncate text-sm font-medium">
                {session.details.name}
              </h1>
              <p className="text-xs text-muted-foreground">
                {session.details.revision === null
                  ? "No completed revision"
                  : `Revision ${session.details.revision}`}
                {running ? " · Agent running" : ""}
              </p>
            </div>
            <span className="text-xs text-muted-foreground capitalize">
              {session.details.status}
            </span>
            {session.details.revision !== null && (
              <Button
                type="button"
                variant="outline"
                size="sm"
                aria-expanded={diffOpen}
                onClick={() => setDiffOpen((open) => !open)}
              >
                <FilesIcon data-icon="inline-start" />
                {diffOpen ? "Hide changes" : "Changes"}
              </Button>
            )}
            {publication && (
              <Button
                variant="outline"
                size="sm"
                onClick={() =>
                  window.open(
                    publication.pullRequest.url,
                    "_blank",
                    "noopener,noreferrer"
                  )
                }
              >
                PR #{publication.pullRequest.number}
                <ExternalLinkIcon data-icon="inline-end" />
              </Button>
            )}
            {!publicationIsCurrent && session.details.revision !== null && (
              <Button
                size="sm"
                disabled={running || session.pending || session.publishing}
                onClick={() => void session.publish()}
              >
                <GitPullRequestIcon data-icon="inline-start" />
                {session.publishing
                  ? "Publishing…"
                  : publication
                    ? "Update PR"
                    : "Create draft PR"}
              </Button>
            )}
          </div>
        </header>

        <main className="min-h-0 flex-1 overflow-y-auto px-4 py-6">
          <div className="mx-auto max-w-4xl space-y-8">
            {session.details.turns.length > 10 && (
              <p className="text-center text-xs text-muted-foreground">
                Showing the latest 10 turns.
              </p>
            )}
            {session.details.turns.slice(-10).map((turn) => (
              <TurnTimeline
                key={turn.id}
                turn={turn}
                events={session.events.get(turn.id) ?? []}
              />
            ))}
            {session.error && (
              <p className="text-sm text-destructive">{session.error}</p>
            )}
          </div>
        </main>

        <PromptComposer
          disabled={running || session.pending || session.publishing}
          pending={session.pending}
          onSend={session.send}
        />
      </div>
      {diffOpen && session.details.revision !== null && (
        <SessionDiffPanel
          key={session.details.revision}
          request={request}
          workspaceId={workspaceId}
          sessionId={sessionId}
          revision={session.details.revision}
          onClose={() => setDiffOpen(false)}
        />
      )}
    </div>
  )
}

function TurnTimeline({
  turn,
  events,
}: {
  turn: SessionTurn
  events: readonly SessionEvent[]
}) {
  const items = useMemo(() => timeline(events), [events])
  return (
    <section aria-label={`Turn ${turn.ordinal}`} className="space-y-3">
      <div className="flex items-center gap-2 text-xs text-muted-foreground">
        <span>Turn {turn.ordinal}</span>
        <span>·</span>
        <span className="capitalize">{turn.status}</span>
        <time dateTime={turn.createdAt}>{formatTime(turn.createdAt)}</time>
      </div>
      {items.map((item) =>
        item.kind === "message" ? (
          <article
            key={item.key}
            className={
              item.role === "user"
                ? "ml-auto max-w-[85%] border bg-muted px-4 py-3"
                : "max-w-none px-1 py-2"
            }
          >
            <div className="mb-2 flex items-center gap-2 text-xs text-muted-foreground">
              {item.role === "user" ? (
                <UserIcon className="size-3.5" />
              ) : (
                <BotIcon className="size-3.5" />
              )}
              <span>{item.role === "user" ? "You" : "AI Sloth"}</span>
              <time dateTime={item.occurredAt}>
                {formatTime(item.occurredAt)}
              </time>
            </div>
            <p className="text-sm leading-6 whitespace-pre-wrap">{item.text}</p>
          </article>
        ) : item.kind === "tool" ? (
          <ToolTimelineItem
            key={`${item.key}-${item.diff ? "diff" : "tool"}`}
            item={item}
          />
        ) : (
          <p key={item.key} className="text-sm text-destructive">
            Agent stopped: {item.code}
          </p>
        )
      )}
      {events.length === 0 &&
        (turn.status === "running" || turn.status === "finalizing") && (
          <p className="text-sm text-muted-foreground">Preparing agent…</p>
        )}
    </section>
  )
}

function ToolTimelineItem({
  item,
}: {
  item: Extract<TimelineItem, { kind: "tool" }>
}) {
  const [diffOpen, setDiffOpen] = useState(true)
  return (
    <div className="space-y-2">
      <details className="border bg-muted/30 px-3 py-2">
        <summary className="flex cursor-pointer items-center gap-2 text-xs">
          <TerminalIcon className="size-3.5" />
          <span className="font-mono">{item.name}</span>
          <span className="text-muted-foreground">
            {item.failed ? "failed" : item.finished ? "completed" : "running"}
          </span>
        </summary>
        {Object.keys(item.input).length > 0 && (
          <pre className="mt-3 overflow-x-auto text-xs whitespace-pre-wrap">
            {JSON.stringify(item.input, null, 2)}
          </pre>
        )}
        {item.output && (
          <pre className="mt-3 max-h-80 overflow-auto border-t pt-3 text-xs whitespace-pre-wrap">
            {item.output}
          </pre>
        )}
      </details>
      {item.diff && (
        <details
          className="border bg-card px-3 py-2"
          open={diffOpen}
          onToggle={(event) => setDiffOpen(event.currentTarget.open)}
        >
          <summary className="flex cursor-pointer items-center gap-2 text-xs">
            <FilesIcon className="size-3.5" />
            <span>Changes</span>
            {typeof item.input.path === "string" && (
              <span className="truncate font-mono text-muted-foreground">
                {item.input.path}
              </span>
            )}
          </summary>
          <div className="mt-3 max-h-96 overflow-auto border-t pt-3">
            {item.diffTruncated || !isUnifiedPatch(item.diff) ? (
              <pre className="text-xs whitespace-pre-wrap">
                {item.diff}
                {item.diffTruncated ? "\n… diff truncated" : ""}
              </pre>
            ) : (
              <Suspense
                fallback={
                  <pre className="text-xs whitespace-pre-wrap">
                    Loading diff…
                  </pre>
                }
              >
                <InlineDiff patch={item.diff} />
              </Suspense>
            )}
          </div>
        </details>
      )}
    </div>
  )
}

function PromptComposer({
  disabled,
  pending,
  onSend,
}: {
  disabled: boolean
  pending: boolean
  onSend(prompt: string): Promise<boolean>
}) {
  const [prompt, setPrompt] = useState("")

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const value = prompt.trim()
    if (!value || disabled) return
    if (await onSend(value)) setPrompt("")
  }

  return (
    <div className="shrink-0 border-t bg-background px-4 py-4">
      <form
        className="mx-auto flex max-w-3xl items-end gap-2 border bg-card p-2 shadow-sm"
        onSubmit={submit}
      >
        <textarea
          value={prompt}
          onChange={(event) => setPrompt(event.target.value)}
          className="max-h-48 min-h-12 flex-1 resize-y bg-transparent px-2 py-2 text-sm outline-none"
          placeholder={
            disabled ? "Wait for the current turn to finish" : "Ask AI Sloth…"
          }
          maxLength={16 * 1024}
          disabled={disabled}
          aria-label="Prompt"
        />
        <Button type="submit" disabled={disabled || !prompt.trim()}>
          {pending ? "Sending…" : "Send"}
        </Button>
      </form>
    </div>
  )
}

function timeline(events: readonly SessionEvent[]): TimelineItem[] {
  const items: TimelineItem[] = []
  const messages = new Map<string, Extract<TimelineItem, { kind: "message" }>>()
  const tools = new Map<string, Extract<TimelineItem, { kind: "tool" }>>()

  for (const event of events) {
    switch (event.type) {
      case "user_message":
        items.push({
          kind: "message",
          key: `user-${event.sequence}`,
          role: "user",
          text: event.text,
          occurredAt: event.occurredAt,
        })
        break
      case "assistant_message_started": {
        const message = {
          kind: "message" as const,
          key: `assistant-${event.messageId}`,
          role: "assistant" as const,
          text: "",
          occurredAt: event.occurredAt,
        }
        messages.set(event.messageId, message)
        items.push(message)
        break
      }
      case "assistant_text_delta": {
        const message = messages.get(event.messageId)
        if (message) message.text += event.text
        break
      }
      case "tool_started": {
        const tool = {
          kind: "tool" as const,
          key: `tool-${event.toolCallId}`,
          name: event.toolName,
          input: event.input,
          output: "",
          finished: false,
          failed: false,
          diff: undefined,
          diffTruncated: false,
        }
        tools.set(event.toolCallId, tool)
        items.push(tool)
        break
      }
      case "tool_output": {
        const tool = tools.get(event.toolCallId)
        if (tool)
          tool.output = event.append ? tool.output + event.text : event.text
        break
      }
      case "tool_finished": {
        const tool = tools.get(event.toolCallId)
        if (tool) {
          tool.finished = true
          tool.failed = event.isError
          tool.diff = event.diff
          tool.diffTruncated = event.diffTruncated ?? false
        }
        break
      }
      case "agent_error":
        items.push({
          kind: "error",
          key: `error-${event.sequence}`,
          code: event.code,
        })
        break
      case "assistant_message_finished":
      case "retry_started":
      case "retry_finished":
      case "compaction_started":
      case "compaction_finished":
        break
    }
  }
  return items
}

type TimelineItem =
  | {
      kind: "message"
      key: string
      role: "user" | "assistant"
      text: string
      occurredAt: string
    }
  | {
      kind: "tool"
      key: string
      name: string
      input: Record<string, string | number>
      output: string
      finished: boolean
      failed: boolean
      diff?: string
      diffTruncated: boolean
    }
  | { kind: "error"; key: string; code: string }

function formatTime(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.valueOf())
    ? ""
    : date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })
}

function CenteredMessage({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex flex-1 items-center justify-center p-6 text-sm text-muted-foreground">
      <div className="text-center">{children}</div>
    </div>
  )
}
