import { clockLabel, useNow } from "@/components/clock"
import { NookStateDot } from "@/components/nook-state"

function Elapsed({ since }: { since: number }) {
  const now = useNow()
  return (
    <span className="tabular-nums">{clockLabel((now - since) / 1000)}</span>
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
