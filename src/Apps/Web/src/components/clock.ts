import { useSyncExternalStore } from "react"

// One shared clock for everything that counts time on screen: it ticks once a second, and only
// while something shown reads it.
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

/** The time now, in milliseconds, once a second. */
export function useNow(): number {
  return useSyncExternalStore(subscribe, () => now)
}

/** Seconds as a clock shows them: 4:07. */
export function clockLabel(seconds: number): string {
  const whole = Math.max(0, Math.floor(seconds))
  return `${Math.floor(whole / 60)}:${String(whole % 60).padStart(2, "0")}`
}
