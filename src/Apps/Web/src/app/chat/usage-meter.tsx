import { useQuery } from "@tanstack/react-query"
import { TimerIcon } from "@phosphor-icons/react"
import { cn } from "cn"

import { clockLabel, useNow } from "@/components/clock"
import { Button } from "@/components/ui/button"
import {
  Popover,
  PopoverContent,
  PopoverHeader,
  PopoverTitle,
  PopoverTrigger,
} from "@/components/ui/popover"
import { type Schemas } from "../api"
import { dollarsLabel, tokensLabel } from "../models"
import {
  PlanMeterList,
  limitSentence,
  nearLimit,
  usePlanUsage,
} from "../plan-usage"
import { usage as usageQuery } from "../queries"
import { BoxLine } from "./box-line"
import type { Transcript } from "./transcript"

// What the chat's agent has used, beside its message box's action: how full its context is, what the
// chat has cost so far, and while its prompt's cache stays, for how long, so a message sent in time
// reads it cheaply. A click shows the details, and what the chat's plan has used of its limits.
// Nothing shows before the agent's first call.

// Under a minute left, the cache countdown asks for attention.
const SoonSeconds = 60

export function UsageMeter({
  chatId,
  transcript,
  workspaceId,
  account,
}: {
  chatId: string
  transcript: Transcript
  workspaceId: string
  /** The chat's account, when the person can see it. */
  account: Schemas["AgentAccountSummary"] | undefined
}) {
  const working = transcript.working
  const usage = useQuery({
    ...usageQuery(chatId, transcript.checkpoint),
    refetchInterval: working ? 4000 : false,
  }).data
  const plan = usePlanUsage(workspaceId, account, working)
  const now = useNow()
  // The agent's own count when it gives one, the latest call's otherwise.
  const context = transcript.context ?? contextOf(usage)
  const warmFor = usage?.cacheWarmUntil
    ? (Date.parse(usage.cacheWarmUntil) - now) / 1000
    : 0
  if (!usage || (usage.calls === 0 && !context)) {
    return null
  }

  const share = context ? context.used / context.size : null
  return (
    <Popover>
      <PopoverTrigger
        render={
          <Button
            variant="ghost"
            size="sm"
            className="gap-2 px-2 font-normal text-muted-foreground tabular-nums"
            aria-label="Usage"
          />
        }
      >
        {share !== null && <ContextRing share={share} />}
        {usage.cost !== null && (
          <span>{dollarsLabel(usage.cost, usage.estimate)}</span>
        )}
        {warmFor > 0 && (
          <span
            className={cn(
              "flex items-center gap-1",
              warmFor < SoonSeconds && "text-warning"
            )}
          >
            <TimerIcon className="size-3.5" />
            {clockLabel(warmFor)}
          </span>
        )}
      </PopoverTrigger>
      <PopoverContent align="end" className="w-80">
        <PopoverHeader>
          <PopoverTitle>This chat</PopoverTitle>
        </PopoverHeader>
        <UsageDetails usage={usage} context={context} warmFor={warmFor} />
        {account && (plan.data?.meters.length || plan.isError) ? (
          <section className="flex flex-col gap-2 border-t pt-3">
            <h3 className="text-sm font-medium">{account.name}</h3>
            {plan.isError ? (
              <p className="text-sm text-muted-foreground">
                Its use of its limits couldn't be read.
              </p>
            ) : (
              <PlanMeterList meters={plan.data?.meters ?? []} now={now} />
            )}
          </section>
        ) : null}
      </PopoverContent>
    </Popover>
  )
}

/** A line on the message box while the chat's plan is near one of its limits, or at it. */
export function PlanLimitLine({
  workspaceId,
  account,
  working,
}: {
  workspaceId: string
  account: Schemas["AgentAccountSummary"] | undefined
  working: boolean
}) {
  const plan = usePlanUsage(workspaceId, account, working)
  const now = useNow()
  const [fullest] = nearLimit(plan.data?.meters ?? [], now)
  if (!account || !fullest) {
    return null
  }
  return (
    <BoxLine tone="warning">{limitSentence(account, fullest, now)}</BoxLine>
  )
}

function UsageDetails({
  usage,
  context,
  warmFor,
}: {
  usage: Schemas["ChatUsage"]
  context: { used: number; size: number } | null
  warmFor: number
}) {
  const tokens = usage.tokens
  const input = tokens.input + tokens.cacheRead + tokens.cacheWrite
  const cached = input > 0 ? Math.round((tokens.cacheRead / input) * 100) : 0
  return (
    <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 text-sm">
      {context && (
        <Detail term="Context">
          {tokensLabel(context.used)} of {tokensLabel(context.size)} tokens
        </Detail>
      )}
      <Detail term="Cost">
        {usage.cost === null
          ? "Not known for this account"
          : usage.estimate
            ? `${dollarsLabel(usage.cost, true)} at the API's prices`
            : dollarsLabel(usage.cost)}
      </Detail>
      <Detail term="Calls">{usage.calls}</Detail>
      <Detail term="Read">
        {tokensLabel(input)} tokens, {cached}% from the cache
      </Detail>
      <Detail term="Written">
        {tokensLabel(tokens.output)} tokens
        {tokens.reasoning > 0 && `, ${tokensLabel(tokens.reasoning)} thinking`}
      </Detail>
      <Detail term="Cache">
        {warmFor > 0
          ? `Warm for ${clockLabel(warmFor)}: a message sent by then reads it cheaply`
          : usage.cacheWarmUntil
            ? usage.rereadCost !== null
              ? `Gone: the next message reads the context again (${dollarsLabel(usage.rereadCost, true)})`
              : "Gone: the next message reads the context again"
            : "Not known for this model"}
      </Detail>
      {usage.turns.length > 0 && (
        <Detail term="Turns">
          <ol className="flex flex-col gap-0.5">
            {usage.turns.slice(0, 5).map((turn, index) => (
              <li key={turn.turn} className="flex justify-between gap-3">
                <span className="text-muted-foreground">
                  {index === 0 ? "Latest" : `${index + 1} back`}, {turn.calls}{" "}
                  {turn.calls === 1 ? "call" : "calls"}
                </span>
                {turn.cost !== null && (
                  <span>{dollarsLabel(turn.cost, turn.estimate)}</span>
                )}
              </li>
            ))}
          </ol>
        </Detail>
      )}
    </dl>
  )
}

function Detail({
  term,
  children,
}: {
  term: string
  children: React.ReactNode
}) {
  return (
    <>
      <dt className="text-muted-foreground">{term}</dt>
      <dd className="min-w-0 tabular-nums">{children}</dd>
    </>
  )
}

// A small ring that fills as the context does: quiet until most of it is used.
function ContextRing({ share }: { share: number }) {
  const filled = Math.min(1, Math.max(0, share))
  const circumference = 2 * Math.PI * 6
  return (
    <svg
      viewBox="0 0 16 16"
      className={cn(
        "size-4 -rotate-90",
        filled >= 0.95
          ? "text-destructive"
          : filled >= 0.8
            ? "text-warning"
            : "text-muted-foreground"
      )}
      aria-label={`Context ${Math.round(filled * 100)}% full`}
      role="img"
    >
      <circle
        cx="8"
        cy="8"
        r="6"
        fill="none"
        stroke="currentColor"
        strokeOpacity="0.25"
        strokeWidth="2"
      />
      <circle
        cx="8"
        cy="8"
        r="6"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeDasharray={`${filled * circumference} ${circumference}`}
      />
    </svg>
  )
}

function contextOf(
  usage: Schemas["ChatUsage"] | undefined
): { used: number; size: number } | null {
  return usage?.contextUsed && usage.contextWindow
    ? { used: usage.contextUsed, size: usage.contextWindow }
    : null
}
