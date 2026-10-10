import * as React from "react"
import { FileDiff, WorkerPoolContextProvider } from "@pierre/diffs/react"
import { cn } from "cn"

import { Button } from "@/components/ui/button"
import { diffOptions, parseChange, workerPool } from "./setup"
import type { DiffProps } from "./types"

// A diff longer than this starts folded, so one big edit doesn't bury a conversation.
const foldedLines = 14
const lineHeight = 20

export default function Diff({
  layout = "unified",
  className,
  ...change
}: DiffProps) {
  const [unfolded, setUnfolded] = React.useState(false)
  const diff = parseChange(change)
  const folded = !unfolded && diff.unifiedLineCount > foldedLines

  return (
    <WorkerPoolContextProvider {...workerPool}>
      <div
        className={cn(
          "relative overflow-hidden rounded-xl bg-card py-1 shadow-raised",
          className
        )}
      >
        <div
          style={folded ? { maxHeight: foldedLines * lineHeight } : undefined}
          className={cn(folded && "overflow-hidden")}
        >
          <FileDiff
            fileDiff={diff}
            options={{
              ...diffOptions,
              diffStyle: layout,
              overflow: "wrap",
              disableFileHeader: true,
            }}
          />
        </div>
        {folded && (
          <div className="absolute inset-x-0 bottom-0 flex h-20 items-end justify-center bg-linear-to-t from-card via-card/85 to-transparent pb-3">
            <Button
              variant="outline"
              size="xs"
              onClick={() => setUnfolded(true)}
            >
              Show all {diff.unifiedLineCount} lines
            </Button>
          </div>
        )}
      </div>
    </WorkerPoolContextProvider>
  )
}
