import * as React from "react"
import { FileTree as TreeView, useFileTree } from "@pierre/trees/react"

import { comparePaths } from "./order"
import { treeCSS } from "./setup"
import type { FileTreeProps } from "./types"

type TreeModel = ReturnType<typeof useFileTree>["model"]

const nothingOnScreen: readonly string[] = []

// The tree library keeps its own model, created once; later props are pushed into it, and its
// selection is reported to the latest onSelect. Syncing a library model is what effects are for, so
// this file is allowed them.
export default function FileTree({
  paths,
  changes,
  selected,
  onScreen = nothingOnScreen,
  onSelect,
  search = false,
  className,
}: FileTreeProps) {
  const latestOnSelect = React.useRef(onSelect)
  const latestSelected = React.useRef(selected)
  // The model reports every selection change, including the ones made here to show `selected`;
  // only the reader's own picks go to onSelect.
  const selecting = React.useRef(false)
  const wrapper = React.useRef<HTMLDivElement>(null)
  const { model } = useFileTree({
    paths,
    gitStatus: changes,
    sort: comparePaths,
    initialExpansion: "open",
    initialSelectedPaths: selected ? [selected] : [],
    icons: { set: "standard", colored: false },
    flattenEmptyDirectories: true,
    search,
    unsafeCSS: treeCSS,
    onSelectionChange: (selection) => {
      if (selecting.current) return
      const path = selection.at(-1)
      if (path && !path.endsWith("/")) {
        latestOnSelect.current?.(path)
        return
      }
      // A click on a folder opens or closes it; the selection stays on the selected file.
      queueMicrotask(() => select(model, latestSelected.current, selecting))
    },
  })
  const shownPaths = React.useRef(paths)
  // Drawing the tree builds a lot of DOM. Its first render is an empty frame and the tree follows
  // in a background render, so whatever opened it paints at once.
  const mounted = React.useDeferredValue(true, false)

  React.useEffect(() => {
    latestOnSelect.current = onSelect
    latestSelected.current = selected
  }, [onSelect, selected])

  React.useEffect(() => {
    if (shownPaths.current === paths) return
    shownPaths.current = paths
    model.resetPaths(paths)
  }, [model, paths])

  React.useEffect(() => {
    model.setGitStatus(changes ?? [])
  }, [model, changes])

  React.useEffect(() => {
    select(model, selected, selecting)
    if (selected)
      model.scrollToPath(selected, { offset: "nearest", focus: false })
  }, [model, selected])

  // The line beside the rows on screen comes from a style of our own in the tree's shadow root: it
  // marks rows by their paths, which the library's styling has no way to say.
  React.useEffect(() => {
    const root = wrapper.current?.firstElementChild?.shadowRoot
    if (!mounted || !root) return
    const style = document.createElement("style")
    style.textContent = onScreenCSS(model, onScreen)
    root.append(style)
    return () => style.remove()
  }, [model, mounted, onScreen])

  // The library sets display inline on its own element, so a class like `hidden` would lose there;
  // the caller's classes go on a wrapper the tree fills.
  return (
    <div ref={wrapper} className={className}>
      {mounted && <TreeView model={model} className="size-full" />}
    </div>
  )
}

function select(
  model: TreeModel,
  path: string | undefined,
  selecting: { current: boolean }
) {
  const current = model.getSelectedPaths()
  const wanted = path ? [path] : []
  if (current.length === wanted.length && current[0] === wanted[0]) return
  selecting.current = true
  for (const selectedPath of current) model.getItem(selectedPath)?.deselect()
  if (path) model.getItem(path)?.select()
  selecting.current = false
}

// A line beside the rows from the first file on screen to the last, folders between them included.
function onScreenCSS(model: TreeModel, files: readonly string[]): string {
  if (files.length === 0) return ""
  const rows = model.getVisibleRows(0, model.getVisibleCount() - 1)
  const shown = rows.filter((row) => files.includes(row.path))
  const first = shown.at(0)
  const last = shown.at(-1)
  if (!first || !last) return ""
  const selector = (path: string) => `[data-item-path="${CSS.escape(path)}"]`
  const run = rows
    .filter((row) => row.index >= first.index && row.index <= last.index)
    .map((row) => selector(row.path))
  return `
    :is(${run.join(", ")})::after {
      content: ""; position: absolute; inset-block: 0; inset-inline-start: -7px; width: 3px;
      background: color-mix(in oklch, var(--foreground) 30%, transparent); pointer-events: none;
    }
    ${selector(first.path)}::after { inset-block-start: 6px; border-start-start-radius: 2px; border-start-end-radius: 2px; }
    ${selector(last.path)}::after { inset-block-end: 6px; border-end-start-radius: 2px; border-end-end-radius: 2px; }
  `
}
