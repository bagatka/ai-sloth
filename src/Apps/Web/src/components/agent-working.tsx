import * as React from "react"

// One shared clock for everything showing elapsed time: it ticks once a second, and only while
// something on screen shows it.
const listeners = new Set<() => void>()
let now = Date.now()
let ticker: number | undefined

function subscribe(listener: () => void) {
  listeners.add(listener)
  if (ticker === undefined) {
    now = Date.now()
    ticker = window.setInterval(() => {
      now = Date.now()
      listeners.forEach((notify) => notify())
    }, 1000)
  }
  return () => {
    listeners.delete(listener)
    if (listeners.size === 0) {
      window.clearInterval(ticker)
      ticker = undefined
    }
  }
}

function Elapsed({ since }: { since: number }) {
  const current = React.useSyncExternalStore(subscribe, () => now)
  const seconds = Math.max(0, Math.floor((current - since) / 1000))
  const minutes = Math.floor(seconds / 60)
  return (
    <span className="tabular-nums">
      {minutes}:{String(seconds % 60).padStart(2, "0")}
    </span>
  )
}

/** What an agent is doing now: the command it runs, or what it's at, and the end of its output. */
export type AgentActivity = {
  /** A command it runs, shown as typed at a prompt. */
  command?: string
  /** What it's doing when it isn't running a command. */
  doing?: string
  /** The last lines of what it printed. */
  output?: string
}

// The one bold surface in the app: while an agent works, its latest output sits on the landing
// page's algae panel, which drifts slowly. The terminal keeps its own dark colors in both themes.
export function AgentWorking({
  agent,
  since,
  activity,
}: {
  /** The agent's name, such as "Claude Code". */
  agent: string
  /** When its turn started, in milliseconds since the epoch. */
  since: number
  activity: AgentActivity
}) {
  return (
    <div
      role="status"
      className="algae-panel algae-alive w-full max-w-xl rounded-2xl p-3 sm:p-4"
    >
      <div className="overflow-hidden rounded-xl border border-white/8 bg-[#0e130d] font-mono text-[13px] shadow-[0_18px_40px_-20px_rgb(14_40_6/0.7)]">
        <div className="flex h-9 items-center justify-between gap-3 border-b border-white/6 px-3.5 text-[#7a8475]">
          <span className="truncate">{agent} is working</span>
          <Elapsed since={since} />
        </div>
        <pre className="max-h-40 overflow-hidden px-3.5 py-3 leading-relaxed whitespace-pre-wrap text-[#a7b2a1]">
          {activity.command !== undefined ? (
            <>
              <span className="text-[#9ccf6a]">$ </span>
              <span className="font-semibold text-[#eef3e8]">
                {activity.command}
              </span>
            </>
          ) : (
            <span className="text-[#eef3e8]">
              {activity.doing ?? "Thinking"}
            </span>
          )}
          {activity.output ? "\n" + activity.output : null}
          <span className="ml-px inline-block h-[1.15em] w-[0.6em] animate-[blink_1.1s_steps(1)_infinite] bg-[#9ccf6a] align-text-bottom will-change-[opacity] motion-reduce:animate-none" />
        </pre>
      </div>
    </div>
  )
}
