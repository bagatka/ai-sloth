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

// The turn in the showcase started 72 seconds before the page opened.
const started = Date.now() - 72_000

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

// The one bold surface in the app: while an agent works, its latest output sits on the landing
// page's algae panel, which drifts slowly. The terminal keeps its own dark colors in both themes.
export function AgentWorking() {
  return (
    <div
      role="status"
      className="algae-panel algae-alive w-full max-w-xl rounded-2xl p-3 sm:p-4"
    >
      <div className="overflow-hidden rounded-xl border border-white/8 bg-[#0e130d] font-mono text-[13px] shadow-[0_18px_40px_-20px_rgb(14_40_6/0.7)]">
        <div className="flex h-9 items-center justify-between border-b border-white/6 px-3.5 text-[#7a8475]">
          <span>Claude Code is working</span>
          <Elapsed since={started} />
        </div>
        <pre className="px-3.5 py-3 leading-relaxed whitespace-pre-wrap text-[#a7b2a1]">
          <span className="text-[#9ccf6a]">$ </span>
          <span className="font-semibold text-[#eef3e8]">
            npm test -- orders
          </span>
          {
            "\n PASS  src/orders/refund.test.ts\n RUNS  src/orders/export.test.ts"
          }
          <span className="ml-px inline-block h-[1.15em] w-[0.6em] animate-[blink_1.1s_steps(1)_infinite] bg-[#9ccf6a] align-text-bottom will-change-[opacity] motion-reduce:animate-none" />
        </pre>
      </div>
    </div>
  )
}
