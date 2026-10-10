import * as React from "react"
import { FileTree as TreeView, useFileTree } from "@pierre/trees/react"

import { compareFilePaths, comparePaths } from "./order"
import { treeCSS } from "./setup"
import type { FileTreeProps } from "./types"

type TreeModel = ReturnType<typeof useFileTree>["model"]

const nothingSelected: readonly string[] = []

// The tree library keeps its own model, created once; later props are pushed into it, and its
// selection is reported to the latest onSelect. Syncing a library model is what effects are for, so
// this file is allowed them.
export default function FileTree({
  paths,
  changes,
  selected = nothingSelected,
  onSelect,
  search = false,
  className,
}: FileTreeProps) {
  const latestOnSelect = React.useRef(onSelect)
  const latestSelected = React.useRef(selected)
  // The model reports every selection change, including the ones made here to show `selected`;
  // only the reader's own picks go to onSelect.
  const showing = React.useRef(false)
  const { model } = useFileTree({
    paths,
    gitStatus: changes,
    sort: comparePaths,
    initialExpansion: "open",
    initialSelectedPaths: selected,
    icons: { set: "standard", colored: false },
    flattenEmptyDirectories: true,
    search,
    unsafeCSS: treeCSS,
    onSelectionChange: (selection) => {
      if (showing.current) return
      const path = selection.at(-1)
      if (path && !path.endsWith("/")) latestOnSelect.current?.(path)
      // The selection belongs to the caller: once the tree finishes its own update, it goes back
      // to `selected`, which the caller may have just changed.
      queueMicrotask(() => show(model, latestSelected.current, showing))
    },
  })
  const shownPaths = React.useRef(paths)
  const revealed = React.useRef(selected)
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
    show(model, selected, showing)
    // Keep the selection's leading edge in view: its end while it moves down the tree, its start
    // while it moves up. The tree scrolls only when that row is out of view.
    const first = selected[0]
    const last = selected.at(-1)
    const previous = revealed.current
    revealed.current = selected
    const moved =
      previous.length !== selected.length ||
      selected.some((path, index) => previous[index] !== path)
    if (!first || !last || !moved) return
    const movedUp =
      previous[0] !== undefined && compareFilePaths(first, previous[0]) < 0
    model.scrollToPath(movedUp ? first : last, {
      offset: "nearest",
      focus: false,
    })
  }, [model, selected])

  // The library sets display inline on its own element, so a class like `hidden` would lose there;
  // the caller's classes go on a wrapper the tree fills.
  return (
    <div className={className}>
      {mounted && <TreeView model={model} className="size-full" />}
    </div>
  )
}

// Selects the given files as one band: the rows from the first to the last, folders between them
// included, so a run of files reads as one range.
function show(
  model: TreeModel,
  files: readonly string[],
  showing: { current: boolean }
) {
  const rows =
    files.length === 0
      ? []
      : model.getVisibleRows(0, model.getVisibleCount() - 1)
  const shown = rows.filter((row) => files.includes(row.path))
  const first = shown.at(0)?.index ?? 0
  const last = shown.at(-1)?.index ?? -1
  const paths = shown.length
    ? rows
        .filter((row) => row.index >= first && row.index <= last)
        .map((row) => row.path)
    : files
  const current = model.getSelectedPaths()
  const same =
    current.length === paths.length &&
    paths.every((path) => current.includes(path))
  if (same) return
  showing.current = true
  for (const path of current) model.getItem(path)?.deselect()
  for (const path of paths) model.getItem(path)?.select()
  showing.current = false
}
