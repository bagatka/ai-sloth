import { cn } from "cn"

// Lines added and removed, as "+5 −6": algae first, then red; a zero side is left out.
export function ChangeCounts({
  additions,
  deletions,
  className,
}: {
  additions: number
  deletions: number
  className?: string
}) {
  return (
    <span
      className={cn(
        "flex shrink-0 gap-1.5 text-caption tabular-nums",
        className
      )}
    >
      {additions > 0 && <span className="text-primary">+{additions}</span>}
      {deletions > 0 && <span className="text-destructive">−{deletions}</span>}
    </span>
  )
}
