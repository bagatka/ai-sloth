import { useQuery } from "@tanstack/react-query"
import { cn } from "cn"

import { type Schemas } from "./api"
import { planUsage } from "./queries"

// What a plan has used of its limits, counted the way its vendor counts: ChatGPT's and Claude's
// windows by the percent used, Copilot's requests of those it allows, or the credits a plan without
// limits spent. API keys pay per token, so they have no meters.

type Meter = Schemas["UsageMeter"]
type Account = Schemas["AgentAccountSummary"]

/** From this share of a limit used, a meter asks for attention. */
const NearShare = 0.9

const day = new Intl.DateTimeFormat("en", { month: "short", day: "numeric" })
const amount = new Intl.NumberFormat("en", { maximumFractionDigits: 1 })

/** Whether the account is a plan whose vendor says what it used of its limits: Copilot and Claude do. */
export function isPlan(account: Account): boolean {
  return account.kind === "CopilotPlan" || account.kind === "ClaudePlan"
}

/**
 * ChatGPT shows a plan's usage on its own page only, and the limits it sets this app there; as OpenAI
 * asks of apps that use the plan, they link to it.
 */
export const chatGptUsage = "https://chatgpt.com/settings/usage"

/** Whether the account is a ChatGPT plan, whose usage ChatGPT shows on its own page. */
export function usesChatGptPlan(account: Account): boolean {
  return account.kind === "ChatGptPlan"
}

/** The plan's usage; nothing to read for an API key. While someone works, it's read every minute. */
export function usePlanUsage(
  workspaceId: string,
  account: Account | undefined,
  working = false
) {
  return useQuery({
    ...planUsage(workspaceId, account?.id ?? ""),
    enabled: !!account && isPlan(account),
    refetchInterval: working ? 60_000 : false,
  })
}

/** How much a meter has used now: nothing once it started over since the vendor said. */
function usedOf(meter: Meter, now: number): number {
  return meter.resetsAt && Date.parse(meter.resetsAt) <= now ? 0 : meter.used
}

/** How much of its limit a meter has used, from 0 to 1; null for one without a limit. */
export function shareOf(meter: Meter, now: number): number | null {
  return meter.limit && meter.limit > 0
    ? Math.min(1, usedOf(meter, now) / meter.limit)
    : null
}

/** The meters at or near their limits, the fullest first. */
export function nearLimit(meters: readonly Meter[], now: number): Meter[] {
  return meters
    .filter((meter) => (shareOf(meter, now) ?? 0) >= NearShare)
    .sort((one, other) => shareOf(other, now)! - shareOf(one, now)!)
}

/** A meter's figure: 42%, 12 of 300, or 41.5 credits. */
export function meterFigure(meter: Meter, now: number): string {
  const used = usedOf(meter, now)
  if (meter.unit === "percent") {
    return `${Math.round(used)}%`
  }
  return meter.limit !== null
    ? `${amount.format(used)} of ${amount.format(meter.limit)}`
    : `${amount.format(used)} ${meter.unit}`
}

/** When a meter starts over: in 42 min, in 3 h 10 min, or Nov 1. */
export function resetLabel(resetsAt: string, now: number): string {
  const seconds = (Date.parse(resetsAt) - now) / 1000
  if (seconds < 3600) {
    return `resets in ${Math.max(1, Math.ceil(seconds / 60))} min`
  }
  if (seconds < 86_400) {
    const hours = Math.floor(seconds / 3600)
    const minutes = Math.floor((seconds % 3600) / 60)
    return `resets in ${hours} h${minutes > 0 ? ` ${minutes} min` : ""}`
  }
  return `resets ${day.format(new Date(resetsAt))}`
}

/** The meters in a few words, such as "5 hours 42%, Week 12%". */
export function metersSummary(meters: readonly Meter[], now: number): string {
  return meters
    .map((meter) => `${meter.name} ${meterFigure(meter, now)}`)
    .join(", ")
}

/** A sentence for a meter at or near its limit, on the message box. */
export function limitSentence(
  account: Account,
  meter: Meter,
  now: number
): string {
  const resets = meter.resetsAt ? `, ${resetLabel(meter.resetsAt, now)}` : ""
  if (shareOf(meter, now) === 1) {
    return `${account.name} has used up its ${meter.name.toLowerCase()} limit${resets}.`
  }
  const used =
    meter.unit === "percent"
      ? `${meterFigure(meter, now)} of its ${meter.name.toLowerCase()} limit`
      : `${meterFigure(meter, now)} ${meter.name.toLowerCase()}`
  return `${account.name} has used ${used}${resets}.`
}

/** Each meter on a line: what it counts, how much is used, a bar for one with a limit, and when it starts over. */
export function PlanMeterList({
  meters,
  now,
}: {
  meters: readonly Meter[]
  now: number
}) {
  return (
    <ul className="flex flex-col gap-2.5">
      {meters.map((meter) => {
        const share = shareOf(meter, now)
        return (
          <li key={meter.name} className="flex flex-col gap-1">
            <div className="flex items-baseline justify-between gap-3 text-sm">
              <span>{meter.name}</span>
              <span className="tabular-nums">{meterFigure(meter, now)}</span>
            </div>
            {share !== null && <MeterBar share={share} />}
            {meter.resetsAt && (
              <span className="text-xs text-muted-foreground">
                {resetLabel(meter.resetsAt, now)}
              </span>
            )}
          </li>
        )
      })}
    </ul>
  )
}

function MeterBar({ share }: { share: number }) {
  return (
    <div className="h-1 overflow-hidden rounded-full bg-muted">
      <div
        className={cn(
          "h-full rounded-full",
          share >= 1
            ? "bg-destructive"
            : share >= NearShare
              ? "bg-warning"
              : "bg-primary"
        )}
        style={{ width: `${Math.round(share * 100)}%` }}
      />
    </div>
  )
}
