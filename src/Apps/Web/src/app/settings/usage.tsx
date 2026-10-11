import { useQuery } from "@tanstack/react-query"
import { Link } from "@tanstack/react-router"

import { Skeleton } from "@/components/ui/skeleton"
import { type Schemas } from "../api"
import { useChatItems } from "../chat-list"
import { dollarsLabel, tokensLabel } from "../models"
import { workspaceUsage } from "../queries"
import { NoRows, Row, Rows, Section } from "./section"

// What the workspace's agents cost over the last month: all of it, day by day where the person is,
// and the chats that cost the most. Each call is priced at its account's prices, a plan's as an
// estimate at the vendor's API prices.

const Days = 30
// The costliest chats shown; the rest add up in the total.
const ChatsShown = 10

const dayName = new Intl.DateTimeFormat("en", {
  month: "short",
  day: "numeric",
})
const count = new Intl.NumberFormat("en")

export function UsageSection({
  workspace,
}: {
  workspace: Schemas["WorkspaceSummary"]
}) {
  const usage = useQuery(workspaceUsage(workspace.id, Days))
  const titles = new Map(
    useChatItems(workspace.id).items.map((item) => [item.id, item.title])
  )

  return (
    <Section
      id="usage"
      title="Usage"
      description={`What ${workspace.name}'s agents cost over the last ${Days} days, at each account's prices. A plan's cost is an estimate at the API's prices.`}
    >
      {usage.isPending ? (
        <Skeleton className="h-32 w-full rounded-xl" />
      ) : usage.isError ? (
        <p className="text-label text-muted-foreground">
          Can't read the usage now. Check your connection, then try again.
        </p>
      ) : usage.data.calls === 0 ? (
        <p className="text-label text-muted-foreground">
          No agent has called its model here in the last {Days} days.
        </p>
      ) : (
        <>
          <div className="flex flex-col gap-3 rounded-xl bg-card p-4 shadow-raised">
            <p className="flex flex-wrap items-baseline gap-x-3">
              <span className="text-title tabular-nums">
                {usage.data.cost === null
                  ? "Cost not known"
                  : dollarsLabel(usage.data.cost, usage.data.estimate)}
              </span>
              <span className="text-label text-muted-foreground tabular-nums">
                {count.format(usage.data.calls)} model calls,{" "}
                {tokensLabel(totalTokens(usage.data.tokens))} tokens
              </span>
            </p>
            <DayBars days={usage.data.days} />
            <div className="flex justify-between text-xs text-muted-foreground">
              <span>{dayName.format(dateOf(usage.data.days[0].day))}</span>
              <span>Today</span>
            </div>
          </div>
          <Rows label="Chats by cost">
            {usage.data.chats.slice(0, ChatsShown).map((chat) => (
              <Row
                key={chat.chat}
                title={
                  <Link
                    to="/chats/$chatId"
                    params={{ chatId: chat.chat }}
                    className="underline-offset-4 hover:underline"
                  >
                    {titles.get(chat.chat) ?? "A chat"}
                  </Link>
                }
                detail={`${count.format(chat.calls)} model ${chat.calls === 1 ? "call" : "calls"}`}
              >
                <span className="text-sm tabular-nums">
                  {chat.cost === null
                    ? "Not known"
                    : dollarsLabel(chat.cost, chat.estimate)}
                </span>
              </Row>
            ))}
            {usage.data.chats.length === 0 && <NoRows>No chats yet.</NoRows>}
          </Rows>
        </>
      )}
    </Section>
  )
}

// A bar a day, the oldest first, as tall as its cost against the costliest day's; by calls when no
// cost is known. Each says its day and figures on hover, and all of them to a screen reader.
function DayBars({ days }: { days: readonly Schemas["DayUsage"][] }) {
  const priced = days.some((day) => day.cost !== null && day.cost > 0)
  const measure = (day: Schemas["DayUsage"]) =>
    priced ? (day.cost ?? 0) : day.calls
  const most = Math.max(...days.map(measure), 0)
  const describe = (day: Schemas["DayUsage"]) =>
    `${dayName.format(dateOf(day.day))}: ${
      day.cost === null
        ? "cost not known"
        : dollarsLabel(day.cost, day.estimate)
    }, ${count.format(day.calls)} calls`
  return (
    <div
      role="img"
      aria-label={days
        .filter((day) => day.calls > 0)
        .map(describe)
        .join("; ")}
      className="flex h-20 items-end gap-0.5"
    >
      {days.map((day) => (
        <div
          key={day.day}
          title={describe(day)}
          className="flex h-full min-w-0 flex-1 items-end"
        >
          <div
            className="w-full rounded-sm bg-primary/70"
            style={{
              height:
                most > 0 && measure(day) > 0
                  ? `${Math.max(4, (measure(day) / most) * 100)}%`
                  : "1px",
            }}
          />
        </div>
      ))}
    </div>
  )
}

// A calendar day as the host writes it, 2026-10-11, at midnight where the person is.
function dateOf(day: string): Date {
  const [year, month, date] = day.split("-").map(Number)
  return new Date(year, month - 1, date)
}

// What the calls read and wrote; their thinking is part of what they wrote.
function totalTokens(tokens: Schemas["TokenCounts"]): number {
  return tokens.input + tokens.cacheRead + tokens.cacheWrite + tokens.output
}
