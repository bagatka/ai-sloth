import { cn } from "cn"

export type NookStateName =
  "starting" | "ready" | "working" | "asleep" | "evicted" | "offline" | "failed"

const labels: Record<NookStateName, string> = {
  starting: "Starting",
  ready: "Ready",
  working: "Working",
  asleep: "Asleep",
  evicted: "Evicted",
  offline: "Offline",
  failed: "Failed",
}

// A nook's state as a dot: algae while it can work, grey while it sleeps or is away, red when it
// failed. Only a working nook moves, so motion always means an agent is busy.
export function NookStateDot({
  state,
  className,
}: {
  state: NookStateName
  className?: string
}) {
  return (
    <span
      aria-hidden="true"
      data-state={state}
      className={cn(
        "relative inline-flex size-2 shrink-0 rounded-full",
        "data-[state=ready]:bg-primary data-[state=working]:bg-primary",
        "data-[state=starting]:shadow-[inset_0_0_0_1.5px_var(--primary)]",
        "data-[state=asleep]:bg-muted-foreground/45",
        "data-[state=evicted]:opacity-60 data-[state=evicted]:shadow-[inset_0_0_0_1.5px_var(--muted-foreground)]",
        "data-[state=offline]:shadow-[inset_0_0_0_1.5px_var(--muted-foreground)]",
        "data-[state=failed]:bg-destructive",
        "data-[state=working]:after:absolute data-[state=working]:after:inset-0 data-[state=working]:after:rounded-full data-[state=working]:after:bg-primary data-[state=working]:after:will-change-[scale,opacity] data-[state=working]:motion-safe:after:animate-[halo_1.8s_var(--ease-out)_infinite]",
        className
      )}
    />
  )
}

export function NookState({ state }: { state: NookStateName }) {
  return (
    <span className="inline-flex items-center gap-2 text-sm text-muted-foreground">
      <NookStateDot state={state} />
      {labels[state]}
    </span>
  )
}
