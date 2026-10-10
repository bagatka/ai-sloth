import * as React from "react"
import { FileTree as TreeView, useFileTree } from "@pierre/trees/react"

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
  const { model } = useFileTree({
    paths,
    gitStatus: changes,
    initialExpansion: "open",
    initialSelectedPaths: selected ? [selected] : [],
    icons: { set: "standard", colored: false },
    flattenEmptyDirectories: true,
    search,
    unsafeCSS: treeCSS,
    onSelectionChange: (selection) => {
      const path = selection.at(-1)
      if (path && !path.endsWith("/")) latestOnSelect.current?.(path)
    },
  })
  const shownPaths = React.useRef(paths)

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

  // The library sets display inline on its own element, so a class like `hidden` would lose there;
  // the caller's classes go on a wrapper the tree fills.
  return (
    <div className={className}>
      <TreeView model={model} className="size-full" />
    </div>
  )
}
