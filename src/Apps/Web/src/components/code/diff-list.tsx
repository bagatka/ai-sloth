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

type Extent = { maxScroll: number; viewport: number }

// Where the list is being read, in content pixels: its top edge, except over the last screenful of
// scrolling, where it slides down to the bottom edge. The last files are too short to reach the
// top, and this way each still takes its turn, in order, as the reader scrolls to the end.
function readingLine(scrollTop: number, { maxScroll, viewport }: Extent) {
  const stretch = Math.min(maxScroll, viewport)
  if (stretch <= 0) return scrollTop
  const slid = (scrollTop - (maxScroll - stretch)) / stretch
  return scrollTop + Math.min(1, Math.max(0, slid)) * viewport
}

// The scroll position at which the reading line reaches a point in the content.
function scrollTopFor(point: number, { maxScroll, viewport }: Extent) {
  const stretch = Math.min(maxScroll, viewport)
  if (stretch <= 0 || point <= maxScroll - stretch) return point
  const slid =
    (point * stretch + viewport * (maxScroll - stretch)) / (stretch + viewport)
  return Math.min(maxScroll, slid)
}

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
  // Marks the file being read without a React render on scroll: a rule of our own shows the tint
  // that every file header carries, hidden, for that one file.
  const listId = React.useId()
  const readingStyle = React.useRef<HTMLStyleElement>(null)
  // Sizes kept from resize reports, so scrolling never has to measure.
  // Not handled: changes so tall that the view pages its scroll height (millions of pixels); the
  // file being read would be off near their end.
  const viewportHeight = React.useRef(0)
  const contentHeight = React.useRef(0)
  // A file the reader picked that no scroll position can make the one being read (when the whole
  // list fits on screen). It stays picked until the list scrolls.
  const picked = React.useRef<{ path: string; scrollTop: number } | null>(null)
  const items: CodeViewDiffItem[] = files.map((file) => ({
    id: file.path,
    type: "diff",
    fileDiff: parseChange(file),
  }))

  function extent(): Extent {
    const viewport = viewportHeight.current
    return {
      maxScroll: Math.max(0, contentHeight.current - viewport),
      viewport,
    }
  }

  React.useImperativeHandle(ref, () => ({
    // Scrolls as close to pinning the file at the top as the list allows while it's the one being
    // read: pinned for most files, and for the last few, the place where the reading line meets it.
    showFile(path) {
      const viewer = view.current?.getInstance()
      const index = files.findIndex((file) => file.path === path)
      if (!viewer || index < 0) return
      const size = extent()
      const start = viewer.getTopForItem(path) ?? 0
      const next = files[index + 1]
      // Whole pixels, so the browser's rounding can't land the reading line just short of the file.
      const earliest = Math.ceil(scrollTopFor(start, size))
      const latest = next
        ? Math.floor(
            scrollTopFor(viewer.getTopForItem(next.path) ?? Infinity, size) - 1
          )
        : size.maxScroll
      const target = Math.max(
        0,
        Math.min(Math.max(Math.min(start, size.maxScroll), earliest), latest)
      )
      picked.current = earliest <= latest ? null : { path, scrollTop: target }
      // Scrolled directly: the view's own scrollTo moves a position up by its sticky header.
      scroller.current?.scrollTo({ top: target, behavior: "instant" })
      // A scroll reports itself once the view applies it; a pick it can't scroll to reports now.
      if (picked.current) reportView()
    },
  }))

  function reportView() {
    const viewer = view.current?.getInstance()
    if (!viewer || files.length === 0) return
    const size = extent()
    const top = viewer.getScrollTop()
    const bottom = top + size.viewport
    const tops = files.map(
      (file) => viewer.getTopForItem(file.path) ?? Infinity
    )
    const fileAt = (point: number) => {
      let index = 0
      while (index + 1 < files.length && tops[index + 1] <= point + 1) index++
      return index
    }
    if (picked.current && Math.abs(picked.current.scrollTop - top) > 1)
      picked.current = null
    const current =
      picked.current?.path ?? files[fileAt(readingLine(top, size))].path
    const onScreen: string[] = []
    for (let index = fileAt(top); index < files.length; index++) {
      if (onScreen.length > 0 && tops[index] > bottom - seenHeight) break
      onScreen.push(files[index].path)
    }
    const key = [current, ...onScreen].join("\n")
    if (key === reported.current) return
    reported.current = key
    if (readingStyle.current)
      readingStyle.current.textContent = `[data-diff-list="${CSS.escape(listId)}"] [data-reading-mark="${CSS.escape(current)}"] { display: block; }`
    onViewChange?.({ current, onScreen })
  }

  return (
    <WorkerPoolContextProvider {...workerPool}>
      {/* The code would paint over the scroller's own focus outline, so this lays the ring on top.
          It also watches the list's size and its content's height: what's on screen, and the file
          being read, can change without a scroll. */}
      <style ref={readingStyle} />
      <div
        data-diff-list={listId}
        ref={(node) => {
          const content = scroller.current?.firstElementChild
          if (!mounted || !node || !(content instanceof HTMLElement)) return
          const observer = new ResizeObserver((entries) => {
            for (const entry of entries) {
              if (entry.target === node)
                viewportHeight.current = entry.contentRect.height
              if (entry.target === content)
                contentHeight.current = entry.contentRect.height
            }
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
                <>
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
                  {/* The file being read wears the tint of a selected row across its whole header
                      (the nearest positioned box), so it matches the tree. */}
                  <span
                    data-reading-mark={item.id}
                    aria-hidden="true"
                    className="pointer-events-none absolute inset-0 hidden bg-sidebar-accent"
                  />
                </>
              )
            }
            onScroll={reportView}
            className="size-full overflow-auto rounded-[inherit] outline-none"
          />
        )}
      </div>
    </WorkerPoolContextProvider>
  )
}
