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

// A file counts as on screen once at least a line of it shows (the line height of code views).
const seenHeight = 20

export default function DiffList({
  files,
  layout = "unified",
  onViewChange,
  ref,
  className,
}: DiffListProps) {
  // The view builds a lot of DOM when it mounts. Its first render is an empty frame and the view
  // follows in a background render, so whatever opened it (a click, a panel) paints at once.
  const mounted = React.useDeferredValue(true, false)
  const view = React.useRef<CodeViewHandle<unknown, unknown>>(null)
  const scroller = React.useRef<HTMLDivElement>(null)
  const reported = React.useRef<string | null>(null)
  // The list's height, kept from resize reports so scrolling never has to measure it.
  const viewportHeight = React.useRef(0)
  // Room after the last file, so it can be pinned at the top like any other.
  const [endRoom, setEndRoom] = React.useState(0)
  const items: CodeViewDiffItem[] = files.map((file) => ({
    id: file.path,
    type: "diff",
    fileDiff: parseChange(file),
  }))

  React.useImperativeHandle(ref, () => ({
    showFile(path) {
      view.current?.scrollTo({ type: "item", id: path, behavior: "instant" })
    },
  }))

  function reportView() {
    const viewer = view.current?.getInstance()
    if (!viewer || !onViewChange || files.length === 0) return
    const top = viewer.getScrollTop()
    const bottom = top + viewportHeight.current
    const tops = files.map(
      (file) => viewer.getTopForItem(file.path) ?? Infinity
    )
    let current = 0
    while (current + 1 < files.length && tops[current + 1] <= top + 1) current++
    const onScreen = [files[current].path]
    for (let index = current + 1; index < files.length; index++) {
      if (tops[index] > bottom - seenHeight) break
      onScreen.push(files[index].path)
    }
    const key = onScreen.join("\n")
    if (key === reported.current) return
    reported.current = key
    onViewChange({ current: onScreen[0], onScreen })
  }

  // Leaves exactly enough room after the last file for it to reach the top.
  function fitEndRoom(content: HTMLElement) {
    const viewer = view.current?.getInstance()
    const last = files.at(-1)
    const lastTop = last && viewer?.getTopForItem(last.path)
    const footer = viewer?.getFooterElement()
    if (lastTop === undefined || !footer) return
    const lastHeight = content.offsetHeight - footer.offsetHeight - lastTop
    const room = Math.max(0, Math.round(viewportHeight.current - lastHeight))
    setEndRoom((current) => (Math.abs(current - room) < 2 ? current : room))
  }

  return (
    <WorkerPoolContextProvider {...workerPool}>
      {/* The code would paint over the scroller's own focus outline, so this lays the ring on top.
          It also watches the list's size and its content's height: they decide the room after the
          last file, and what's on screen can change without a scroll. */}
      <div
        ref={(node) => {
          const content = scroller.current?.firstElementChild
          if (!mounted || !node || !(content instanceof HTMLElement)) return
          const observer = new ResizeObserver((entries) => {
            for (const entry of entries) {
              if (entry.target === node)
                viewportHeight.current = entry.contentRect.height
            }
            fitEndRoom(content)
            reportView()
          })
          observer.observe(node)
          observer.observe(content)
          return () => observer.disconnect()
        }}
        className={cn("focus-ring-over size-full rounded-[inherit]", className)}
      >
        {mounted && (
          <CodeView
            ref={view}
            containerRef={(node) => {
              scroller.current = node
              // The view takes its scroller out of the tab order; put it back, so the keyboard
              // can scroll it.
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
            renderCodeViewFooter={() => (
              <p
                style={{ minHeight: endRoom }}
                className="px-4 pt-6 pb-8 text-center text-caption text-muted-foreground"
              >
                End of changes
              </p>
            )}
            onScroll={reportView}
            className="size-full overflow-auto rounded-[inherit] outline-none"
          />
        )}
      </div>
    </WorkerPoolContextProvider>
  )
}
