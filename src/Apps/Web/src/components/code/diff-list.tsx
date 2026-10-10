import * as React from "react"
import type { CodeViewDiffItem } from "@pierre/diffs"
import {
  CodeView,
  WorkerPoolContextProvider,
  type CodeViewHandle,
} from "@pierre/diffs/react"
import { cn } from "cn"

import { ChangeCounts } from "./change-counts"
import { diffOptions, parseChange, workerPool } from "./setup"
import type { DiffListProps } from "./types"

type Viewer = Parameters<
  NonNullable<React.ComponentProps<typeof CodeView>["onScroll"]>
>[1]

export default function DiffList({
  files,
  layout = "unified",
  onFileInView,
  ref,
  className,
}: DiffListProps) {
  // The view builds a lot of DOM when it mounts. Its first render is an empty frame and the view
  // follows in a background render, so whatever opened it (a click, a panel) paints at once.
  const mounted = React.useDeferredValue(true, false)
  const view = React.useRef<CodeViewHandle<unknown, unknown>>(null)
  const scroller = React.useRef<HTMLDivElement>(null)
  // The file in view is the one at the top, except when the reader picked a file near the end,
  // which can't scroll up to the top: it stays the file in view while it's on screen.
  const fileInView = React.useRef(files[0]?.path)
  const items: CodeViewDiffItem[] = files.map((file) => ({
    id: file.path,
    type: "diff",
    fileDiff: parseChange(file),
  }))

  React.useImperativeHandle(ref, () => ({
    showFile(path) {
      fileInView.current = path
      view.current?.scrollTo({ type: "item", id: path, behavior: "instant" })
    },
  }))

  function trackFileInView(scrollTop: number, viewer: Viewer) {
    let top = files[0]?.path
    for (const file of files) {
      const fileTop = viewer.getTopForItem(file.path)
      if (fileTop === undefined || fileTop > scrollTop + 1) break
      top = file.path
    }
    if (top === undefined || top === fileInView.current) return

    const root = scroller.current
    const shownTop =
      fileInView.current === undefined
        ? undefined
        : viewer.getTopForItem(fileInView.current)
    if (root && shownTop !== undefined) {
      const bottom = scrollTop + root.clientHeight
      const atEnd = bottom >= root.scrollHeight - 1
      if (atEnd && shownTop > scrollTop && shownTop < bottom) return
    }
    fileInView.current = top
    onFileInView?.(top)
  }

  // The code would paint over the scroller's own focus outline, so a wrapper lays the ring on top.
  return (
    <WorkerPoolContextProvider {...workerPool}>
      <div
        className={cn("focus-ring-over size-full rounded-[inherit]", className)}
      >
        {mounted && (
          <CodeView
            ref={view}
            containerRef={(node) => {
              scroller.current = node
              // The view takes its scroller out of the tab order; put it back, so the keyboard can
              // scroll it.
              if (!node) return
              node.tabIndex = 0
              node.setAttribute("role", "region")
              node.setAttribute("aria-label", "Changed files")
            }}
            items={items}
            options={{
              ...diffOptions,
              diffStyle: layout,
              overflow: "scroll",
              stickyHeaders: true,
              layout: { paddingTop: 0, paddingBottom: 0, gap: 0 },
            }}
            renderHeaderMetadata={(item) =>
              item.type === "diff" && (
                <ChangeCounts
                  additions={item.fileDiff.hunks.reduce(
                    (sum, hunk) => sum + hunk.additionLines,
                    0
                  )}
                  deletions={item.fileDiff.hunks.reduce(
                    (sum, hunk) => sum + hunk.deletionLines,
                    0
                  )}
                  className="text-label"
                />
              )
            }
            onScroll={trackFileInView}
            className="size-full overflow-auto rounded-[inherit] outline-none"
          />
        )}
      </div>
    </WorkerPoolContextProvider>
  )
}
