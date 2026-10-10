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
  onFilesInView,
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
  const items: CodeViewDiffItem[] = files.map((file) => ({
    id: file.path,
    type: "diff",
    fileDiff: parseChange(file),
  }))

  React.useImperativeHandle(ref, () => ({
    showFile(path) {
      view.current?.scrollTo({ type: "item", id: path, behavior: "instant" })
      // Mark the file's header once the view has drawn it there, so a pick always answers, even
      // when the file is already on screen and the list can't move.
      requestAnimationFrame(() => {
        const header = view.current
          ?.getInstance()
          ?.getRenderedItems()
          .find((item) => item.id === path)
          ?.element.shadowRoot?.querySelector("[data-diffs-header]")
        header?.animate(
          {
            backgroundColor: [
              "color-mix(in oklch, var(--card), var(--primary) 14%)",
              getComputedStyle(header).backgroundColor,
            ],
          },
          { duration: 900, easing: "ease-out" }
        )
      })
    },
  }))

  // Reports the files on screen when they change: scrolling, resizing, or a different layout.
  function reportFilesInView() {
    const viewer = view.current?.getInstance()
    if (!viewer || !onFilesInView) return
    const top = viewer.getScrollTop()
    const bottom = top + viewportHeight.current
    const inView = files.flatMap((file, index) => {
      const next = files[index + 1]
      const start = viewer.getTopForItem(file.path) ?? Infinity
      const end = next
        ? (viewer.getTopForItem(next.path) ?? Infinity)
        : Infinity
      return Math.min(end, bottom) - Math.max(start, top) >= seenHeight
        ? [file.path]
        : []
    })
    const key = inView.join("\n")
    if (key === reported.current) return
    reported.current = key
    onFilesInView(inView)
  }

  return (
    <WorkerPoolContextProvider {...workerPool}>
      {/* The code would paint over the scroller's own focus outline, so this lays the ring on top.
          It also watches the view's size and its content's height for files coming into view
          without a scroll. */}
      <div
        ref={(node) => {
          const content = scroller.current?.firstElementChild
          if (!mounted || !node || !content) return
          const observer = new ResizeObserver((entries) => {
            for (const entry of entries) {
              if (entry.target === node)
                viewportHeight.current = entry.contentRect.height
            }
            reportFilesInView()
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
            onScroll={reportFilesInView}
            className="size-full overflow-auto rounded-[inherit] outline-none"
          />
        )}
      </div>
    </WorkerPoolContextProvider>
  )
}
