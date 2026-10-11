import {
  ArrowsLeftRightIcon,
  BrainIcon,
  CaretRightIcon,
  CheckCircleIcon,
  CircleDashedIcon,
  CircleNotchIcon,
  FileTextIcon,
  GlobeIcon,
  MagnifyingGlassIcon,
  PencilSimpleLineIcon,
  TerminalWindowIcon,
  TrashIcon,
  WarningCircleIcon,
  WrenchIcon,
  XCircleIcon,
  type Icon,
} from "@phosphor-icons/react"
import { cn } from "cn"

import { AgentWorking, type AgentActivity } from "@/components/agent-working"
import { Diff, preloadCode } from "@/components/code"
import { Markdown } from "@/components/markdown"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Bubble, BubbleContent } from "@/components/ui/bubble"
import { Button } from "@/components/ui/button"
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible"
import { Marker, MarkerContent } from "@/components/ui/marker"
import { Message, MessageContent } from "@/components/ui/message"
import {
  MessageScroller,
  MessageScrollerButton,
  MessageScrollerContent,
  MessageScrollerItem,
  MessageScrollerProvider,
  MessageScrollerViewport,
} from "@/components/ui/message-scroller"
import type { ChatState, PendingMessage } from "./chat-stream"
import { useSend } from "./composer"
import type { Row } from "./transcript"

// What "Fix the setup" sends, as the person: the agent already knows the setup failed and where its
// output is.
const fixTheSetup =
  "Fix the setup that failed, run it again, and commit the fix."

type Props = {
  chatId: string
  state: ChatState
  /** The signed-in person, whose messages read as theirs. */
  me: string
  /** Names of the people who wrote in the chat. */
  names: ReadonlyMap<string, string>
  /** The agent's name, such as "Claude Code". */
  agent: string
}

export function TranscriptView({ chatId, state, me, names, agent }: Props) {
  const { transcript, pending } = state
  const send = useSend(chatId)

  return (
    <div className="min-h-0 flex-1">
      <MessageScrollerProvider>
        <MessageScroller>
          <MessageScrollerViewport>
            <MessageScrollerContent className="mx-auto w-full max-w-[44rem] gap-3 px-4 pt-8 pb-10">
              {transcript.rows.map((row, index) => (
                <MessageScrollerItem
                  key={row.key}
                  messageId={row.key}
                  scrollAnchor={row.kind === "message" && row.sentBy === me}
                >
                  {startsAgentOutput(transcript.rows, index) && (
                    <p className="pb-1.5 text-label font-semibold">{agent}</p>
                  )}
                  <TranscriptRow
                    row={row}
                    me={me}
                    names={names}
                    working={transcript.working}
                    fixSetup={
                      pending.length === 0 ? () => send(fixTheSetup) : undefined
                    }
                  />
                </MessageScrollerItem>
              ))}
              {pending.map((message) => (
                <MessageScrollerItem
                  key={message.key}
                  messageId={message.key}
                  scrollAnchor
                >
                  <PendingRow message={message} />
                </MessageScrollerItem>
              ))}
              {transcript.working && transcript.turnStartedAt && (
                <MessageScrollerItem messageId="working">
                  <AgentWorking
                    agent={agent}
                    since={Date.parse(transcript.turnStartedAt)}
                    activity={activityOf(transcript.rows)}
                  />
                </MessageScrollerItem>
              )}
            </MessageScrollerContent>
          </MessageScrollerViewport>
          <MessageScrollerButton />
        </MessageScroller>
      </MessageScrollerProvider>
    </div>
  )
}

// The agent's name shows over the first of its output after someone's message.
function startsAgentOutput(rows: readonly Row[], index: number): boolean {
  const agentRow = (row: Row) =>
    row.kind === "agent-text" ||
    row.kind === "thought" ||
    row.kind === "tool" ||
    row.kind === "plan"
  if (!agentRow(rows[index])) {
    return false
  }
  for (let earlier = index - 1; earlier >= 0; earlier--) {
    if (agentRow(rows[earlier])) {
      return false
    }
    if (
      rows[earlier].kind === "message" ||
      rows[earlier].kind === "turn-ended"
    ) {
      return true
    }
  }
  return true
}

function TranscriptRow({
  row,
  me,
  names,
  working,
  fixSetup,
}: {
  row: Row
  me: string
  names: ReadonlyMap<string, string>
  working: boolean
  /** Asks the agent to fix a failed setup; none while a message is on its way. */
  fixSetup: (() => void) | undefined
}) {
  switch (row.kind) {
    case "message": {
      const mine = row.sentBy === me
      const author = mine ? null : (names.get(row.sentBy) ?? "Someone")
      const note =
        row.state === "cancelled"
          ? "Not sent: the turn was stopped"
          : row.state === "steered"
            ? "Sent into the running turn"
            : row.state === "waiting" && working
              ? "Waits for the turn to end"
              : row.proposal
                ? "Proposed: the account's owner sends it on"
                : null
      return (
        <Message align={mine ? "end" : "start"} className="pt-5">
          <MessageContent className="gap-1">
            {author && <p className="text-label font-semibold">{author}</p>}
            <Bubble
              variant="secondary"
              className={cn(row.state === "cancelled" && "opacity-60")}
            >
              <BubbleContent
                className={cn(
                  "whitespace-pre-wrap",
                  row.state === "cancelled" && "line-through"
                )}
              >
                {row.text}
              </BubbleContent>
            </Bubble>
            {note && (
              <p className="text-caption text-muted-foreground">{note}</p>
            )}
          </MessageContent>
        </Message>
      )
    }
    case "agent-text":
      return <Markdown text={row.text} className="text-read" />
    case "thought":
      return (
        <Collapsible>
          <RowTrigger icon={BrainIcon} label="Thought" />
          <CollapsibleContent>
            <Markdown
              text={row.text}
              className="mt-1 border-l-2 border-border pl-3 text-label text-muted-foreground"
            />
          </CollapsibleContent>
        </Collapsible>
      )
    case "tool":
      return <ToolRow row={row} />
    case "plan":
      return (
        <ul
          className="flex flex-col gap-1.5 rounded-xl bg-card px-3.5 py-3 text-label shadow-raised"
          aria-label="Plan"
        >
          {row.entries.map((entry, index) => (
            <li key={index} className="flex items-start gap-2">
              {entry.status === "completed" ? (
                <CheckCircleIcon
                  weight="fill"
                  className="mt-0.5 size-4 shrink-0 text-primary"
                />
              ) : entry.status === "in_progress" ? (
                <CircleNotchIcon className="mt-0.5 size-4 shrink-0 animate-spin text-primary motion-reduce:animate-none" />
              ) : (
                <CircleDashedIcon className="mt-0.5 size-4 shrink-0 text-muted-foreground" />
              )}
              <span
                className={cn(
                  entry.status === "completed" &&
                    "text-muted-foreground line-through decoration-muted-foreground/50"
                )}
              >
                {entry.content}
              </span>
            </li>
          ))}
        </ul>
      )
    case "turn-ended":
      return row.stopReason === "failed" ? (
        <Alert variant="destructive" className="my-2">
          <WarningCircleIcon />
          <AlertTitle>The turn failed</AlertTitle>
          {row.failure && <AlertDescription>{row.failure}</AlertDescription>}
        </Alert>
      ) : (
        <Marker variant="separator" className="py-3 text-caption">
          <MarkerContent>{turnEnding(row)}</MarkerContent>
        </Marker>
      )
    case "setup":
      if (!row.ended) {
        return (
          <RowLabel
            icon={CircleNotchIcon}
            label="Setting up the nook"
            spinning
          />
        )
      }
      return row.exitCode === 0 ? (
        <Marker variant="separator" className="py-3 text-caption">
          <MarkerContent>
            {row.took === null ? "Set up" : `Set up in ${duration(row.took)}`}
          </MarkerContent>
        </Marker>
      ) : (
        <Alert variant="warning">
          <WarningCircleIcon />
          <AlertTitle>The setup failed (exit code {row.exitCode})</AlertTitle>
          <AlertDescription>
            The agent knows, and can fix it when asked.
          </AlertDescription>
          {!row.followedUp && fixSetup && (
            <div className="col-start-2 mt-2">
              <Button variant="outline" size="sm" onClick={fixSetup}>
                <WrenchIcon />
                Fix the setup
              </Button>
            </div>
          )}
          {row.output && (
            <pre className="col-span-full mt-2 max-h-48 overflow-auto rounded-lg bg-muted px-3 py-2 text-xs">
              {row.output}
            </pre>
          )}
        </Alert>
      )
    case "notice":
      return (
        <Alert variant={row.tone === "warning" ? "warning" : "default"}>
          <WarningCircleIcon />
          <AlertDescription>{row.text}</AlertDescription>
        </Alert>
      )
  }
}

function PendingRow({ message }: { message: PendingMessage }) {
  return (
    <Message align="end" className="pt-5">
      <MessageContent className="gap-1">
        <Bubble variant="secondary" className="opacity-70">
          <BubbleContent className="whitespace-pre-wrap">
            {message.text}
          </BubbleContent>
        </Bubble>
        <p className="text-caption text-muted-foreground">Sending</p>
      </MessageContent>
    </Message>
  )
}

const tools: Record<string, { icon: Icon; verb: string }> = {
  execute: { icon: TerminalWindowIcon, verb: "Ran" },
  edit: { icon: PencilSimpleLineIcon, verb: "Edited" },
  read: { icon: FileTextIcon, verb: "Read" },
  search: { icon: MagnifyingGlassIcon, verb: "Searched" },
  fetch: { icon: GlobeIcon, verb: "Fetched" },
  delete: { icon: TrashIcon, verb: "Deleted" },
  move: { icon: ArrowsLeftRightIcon, verb: "Moved" },
  think: { icon: BrainIcon, verb: "Thought" },
}

// A tool call: one quiet line with what it did, opening into its diffs or its output.
function ToolRow({ row }: { row: Extract<Row, { kind: "tool" }> }) {
  const tool = tools[row.toolKind] ?? { icon: WrenchIcon, verb: "" }
  const running = row.status === "pending" || row.status === "in_progress"
  const seconds =
    row.endedAt === null
      ? null
      : (Date.parse(row.endedAt) - Date.parse(row.at)) / 1000
  const title = row.title.replace(/^`(.*)`$/, "$1")
  const trailing = running ? (
    <CircleNotchIcon
      className="size-3.5 shrink-0 animate-spin motion-reduce:animate-none"
      aria-label="Running"
    />
  ) : row.status === "failed" ? (
    <XCircleIcon
      weight="fill"
      className="size-3.5 shrink-0 text-destructive"
      aria-label="Failed"
    />
  ) : seconds !== null && seconds >= 1 ? (
    <span className="shrink-0 tabular-nums">{duration(seconds)}</span>
  ) : null

  if (row.content.length === 0) {
    return (
      <RowLabel
        icon={tool.icon}
        label={title}
        code={row.toolKind === "execute"}
        trailing={trailing}
      />
    )
  }

  return (
    <Collapsible>
      <RowTrigger
        icon={tool.icon}
        label={title}
        code={row.toolKind === "execute"}
        trailing={trailing}
        onPointerEnter={
          row.content.some((item) => item.type === "diff")
            ? preloadCode
            : undefined
        }
      />
      <CollapsibleContent className="flex flex-col gap-1.5 pt-1.5">
        {row.content.map((item, index) =>
          item.type === "diff" ? (
            <Diff
              key={index}
              path={item.path}
              before={item.oldText ?? ""}
              after={item.newText}
            />
          ) : (
            <pre
              key={index}
              className="max-h-96 overflow-auto rounded-lg bg-muted px-3.5 py-3 text-xs leading-relaxed"
            >
              {item.text}
            </pre>
          )
        )}
      </CollapsibleContent>
    </Collapsible>
  )
}

const rowClasses =
  "-mx-2 flex w-[calc(100%+1rem)] items-center gap-2 rounded-lg px-2 py-1.5 text-left text-label text-muted-foreground [&_code]:bg-transparent [&_code]:p-0 [&_code]:text-caption [&_code]:text-foreground/80"

function RowLabel({
  icon: RowIcon,
  label,
  code,
  trailing,
  spinning,
}: {
  icon: Icon
  label: string
  code?: boolean
  trailing?: React.ReactNode
  spinning?: boolean
}) {
  return (
    <div className={rowClasses}>
      <RowIcon
        className={cn(
          "size-4 shrink-0",
          spinning && "animate-spin motion-reduce:animate-none"
        )}
      />
      {code ? (
        <code className="min-w-0 truncate">{label}</code>
      ) : (
        <span className="min-w-0 truncate">{label}</span>
      )}
      {trailing && (
        <span className="ml-auto flex items-center">{trailing}</span>
      )}
    </div>
  )
}

function RowTrigger({
  icon: RowIcon,
  label,
  code,
  trailing,
  onPointerEnter,
}: {
  icon: Icon
  label: string
  code?: boolean
  trailing?: React.ReactNode
  onPointerEnter?: () => void
}) {
  return (
    <CollapsibleTrigger
      onPointerEnter={onPointerEnter}
      render={
        <button
          className={cn(
            rowClasses,
            "group focus-ring transition-colors hover:bg-muted/70 hover:text-foreground active:bg-[color-mix(in_oklch,var(--muted),var(--foreground)_6%)]"
          )}
        />
      }
    >
      <RowIcon className="size-4 shrink-0" />
      {code ? (
        <code className="min-w-0 truncate">{label}</code>
      ) : (
        <span className="min-w-0 truncate">{label}</span>
      )}
      <span className="ml-auto flex items-center gap-2">
        {trailing}
        <CaretRightIcon className="size-3.5 shrink-0 transition-transform duration-200 ease-out group-data-panel-open:rotate-90" />
      </span>
    </CollapsibleTrigger>
  )
}

// What the agent is doing now: its running tool call, with the end of its output.
function activityOf(rows: readonly Row[]): AgentActivity {
  for (let index = rows.length - 1; index >= 0; index--) {
    const row = rows[index]
    if (row.kind === "turn-ended" || row.kind === "message") {
      break
    }
    if (
      row.kind === "tool" &&
      (row.status === "pending" || row.status === "in_progress")
    ) {
      const output = row.content
        .flatMap((item) => (item.type === "text" ? [item.text] : []))
        .join("\n")
      const tail = output.trimEnd().split("\n").slice(-4).join("\n")
      const title = row.title.replace(/^`(.*)`$/, "$1")
      return row.toolKind === "execute"
        ? { command: title, output: tail }
        : { doing: title, output: tail }
    }
    if (row.kind === "agent-text") {
      return { doing: "Writing" }
    }
  }
  return { doing: "Thinking" }
}

function turnEnding(row: Extract<Row, { kind: "turn-ended" }>): string {
  const took =
    row.startedAt === null
      ? null
      : (Date.parse(row.at) - Date.parse(row.startedAt)) / 1000
  switch (row.stopReason) {
    case "cancelled":
      return "The turn was stopped"
    case "refusal":
      return "The agent declined"
    case "max_tokens":
    case "max_turn_requests":
      return "The agent reached its limit for one turn"
    default:
      return took === null || took < 1
        ? "Turn ended"
        : `Turn ended after ${duration(took, true)}`
  }
}

function duration(seconds: number, words = false): string {
  const whole = Math.max(0, Math.round(seconds))
  if (words) {
    if (whole < 60) return whole === 1 ? "1 second" : `${whole} seconds`
    const minutes = Math.round(whole / 60)
    if (minutes < 60) return minutes === 1 ? "1 minute" : `${minutes} minutes`
    const hours = Math.round(minutes / 60)
    return hours === 1 ? "1 hour" : `${hours} hours`
  }
  if (whole < 60) return `${whole}s`
  const minutes = Math.floor(whole / 60)
  return minutes < 60
    ? `${minutes}m ${whole % 60}s`
    : `${Math.floor(minutes / 60)}h ${minutes % 60}m`
}
