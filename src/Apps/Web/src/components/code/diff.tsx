import * as React from "react"
import { getSingularPatch, parseDiffFromFile } from "@pierre/diffs"
import {
  FileDiff,
  Virtualizer,
  WorkerPoolContextProvider,
} from "@pierre/diffs/react"
import { cn } from "cn"

import { Button } from "@/components/ui/button"
import { ChangeCounts } from "./change-counts"
import { codeCSS, codeTheme, workerPool } from "./setup"
import type { DiffProps } from "./types"

// An inline diff longer than this starts folded, so one big edit doesn't bury a conversation.
const foldedLines = 14
const lineHeight = 20

export default function Diff(props: DiffProps) {
  const { variant = "inline", layout = "unified", className } = props
  const [unfolded, setUnfolded] = React.useState(false)
  const diff =
    "patch" in props
      ? getSingularPatch(props.patch)
      : parseDiffFromFile(
          { name: props.path, contents: props.before },
          { name: props.path, contents: props.after }
        )
  const options = {
    theme: codeTheme,
    diffStyle: layout,
    diffIndicators: "bars",
    lineDiffType: "word-alt",
    // Not handled: expanding unchanged lines from the keyboard. The library draws its expander as
    // a div that can't take focus; fixing that needs a change in @pierre/diffs itself.
    hunkSeparators: "line-info-basic",
    overflow: variant === "inline" ? "wrap" : "scroll",
    disableFileHeader: variant === "inline",
    stickyHeader: variant === "panel",
    unsafeCSS: codeCSS,
  } as const

  if (variant === "panel") {
    return (
      <WorkerPoolContextProvider {...workerPool}>
        <Virtualizer className={cn("size-full overflow-auto", className)}>
          <FileDiff
            fileDiff={diff}
            options={options}
            renderHeaderMetadata={(file) => (
              <ChangeCounts
                additions={file.hunks.reduce(
                  (sum, hunk) => sum + hunk.additionLines,
                  0
                )}
                deletions={file.hunks.reduce(
                  (sum, hunk) => sum + hunk.deletionLines,
                  0
                )}
                className="text-label"
              />
            )}
          />
        </Virtualizer>
      </WorkerPoolContextProvider>
    )
  }

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
          <FileDiff fileDiff={diff} options={options} />
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
