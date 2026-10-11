import * as React from "react"

import { NookStateDot } from "@/components/nook-state"

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

// While an agent works: one quiet line after its output, with how long its turn has run. The
// message box's Stop button says it too, so this stays small. Only "Working" is announced; the
// clock isn't, or screen readers would read it every second.
export function AgentWorking({ since }: { since: number }) {
  return (
    <p className="flex items-center gap-2 text-label text-muted-foreground">
      <NookStateDot state="working" />
      <span role="status">Working</span>
      <Elapsed since={since} />
    </p>
  )
}
