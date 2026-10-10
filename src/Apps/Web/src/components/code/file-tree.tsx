import * as React from "react"
import { FileTree as TreeView, useFileTree } from "@pierre/trees/react"

import { comparePaths } from "./order"
import { treeCSS } from "./setup"
import type { FileTreeProps } from "./types"

// The tree library keeps its own model, created once; later props are pushed into it, and its
// selection is reported to the latest onSelect. Syncing a library model is what effects are for, so
// this file is allowed them.
export default function FileTree({
  paths,
  changes,
  selected,
  onSelect,
  search = false,
  className,
}: FileTreeProps) {
  const latestOnSelect = React.useRef(onSelect)
  // The model reports every selection change, including the ones made here to follow `selected`;
  // only the reader's own picks go to onSelect.
  const following = React.useRef(false)
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
      const path = selection.at(-1)
      if (following.current || !path || path.endsWith("/")) return
      latestOnSelect.current?.(path)
    },
  })
  const shownPaths = React.useRef(paths)
  // Drawing the tree builds a lot of DOM. Its first render is an empty frame and the tree follows
  // in a background render, so whatever opened it paints at once.
  const mounted = React.useDeferredValue(true, false)

  React.useEffect(() => {
    latestOnSelect.current = onSelect
  }, [onSelect])

  React.useEffect(() => {
    if (shownPaths.current === paths) return
    shownPaths.current = paths
    model.resetPaths(paths)
  }, [model, paths])

  React.useEffect(() => {
    model.setGitStatus(changes ?? [])
  }, [model, changes])

  React.useEffect(() => {
    const current = model.getSelectedPaths()
    if (!selected || (current.length === 1 && current[0] === selected)) return
    following.current = true
    for (const path of current) model.getItem(path)?.deselect()
    model.getItem(selected)?.select()
    following.current = false
    model.scrollToPath(selected, { offset: "nearest" })
  }, [model, selected])

  // The library sets display inline on its own element, so a class like `hidden` would lose there;
  // the caller's classes go on a wrapper the tree fills.
  return (
    <div className={className}>
      {mounted && <TreeView model={model} className="size-full" />}
    </div>
  )
}
