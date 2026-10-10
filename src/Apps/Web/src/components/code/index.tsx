import * as React from "react"
import { cn } from "cn"

import { Skeleton } from "@/components/ui/skeleton"
import type {
  CodeFileProps,
  DiffListProps,
  DiffProps,
  FileTreeProps,
} from "./types"

export { ChangeCounts } from "./change-counts"
export { inTreeOrder } from "./order"
export type {
  CodeFileProps,
  DiffLayout,
  DiffListHandle,
  DiffListProps,
  DiffProps,
  FileChange,
  FileChangeStatus,
  FileTreeProps,
} from "./types"

// Code views load on first use: highlighting is heavy, and most screens never show code. Anything
// about to open code (a hover, a menu) calls preloadCode, and a screen that will likely show code
// calls warmUpCode, so the views and the highlighting workers are ready before they're needed.
// Once a view's module has loaded, it renders directly: going through React.lazy and Suspense
// again would hold the view back behind React's fallback throttle (about 300 ms) even though
// nothing is left to load.
let LoadedDiff: React.ComponentType<DiffProps> | undefined
let LoadedDiffList: React.ComponentType<DiffListProps> | undefined
let LoadedCodeFile: React.ComponentType<CodeFileProps> | undefined
let LoadedFileTree: React.ComponentType<FileTreeProps> | undefined

const loadDiff = () =>
  import("./diff").then((module) => ((LoadedDiff = module.default), module))
const loadDiffList = () =>
  import("./diff-list").then(
    (module) => ((LoadedDiffList = module.default), module)
  )
const loadCodeFile = () =>
  import("./code-file").then(
    (module) => ((LoadedCodeFile = module.default), module)
  )
const loadFileTree = () =>
  import("./file-tree").then(
    (module) => ((LoadedFileTree = module.default), module)
  )
const LazyDiff = React.lazy(loadDiff)
const LazyDiffList = React.lazy(loadDiffList)
const LazyCodeFile = React.lazy(loadCodeFile)
const LazyFileTree = React.lazy(loadFileTree)

export function preloadCode() {
  void loadDiff()
  void loadDiffList()
  void loadFileTree()
  void import("./setup").then((setup) => setup.startHighlighting())
}

/** Preloads the code views once the page is idle, so warming up never competes with the page itself. */
export function warmUpCode() {
  if ("requestIdleCallback" in window) {
    requestIdleCallback(preloadCode, { timeout: 3000 })
  } else {
    setTimeout(preloadCode, 1500)
  }
}

export function Diff(props: DiffProps) {
  if (LoadedDiff) return <LoadedDiff {...props} />
  return (
    <React.Suspense fallback={<CodeLoading lines={6} />}>
      <LazyDiff {...props} />
    </React.Suspense>
  )
}

export function DiffList(props: DiffListProps) {
  if (LoadedDiffList) return <LoadedDiffList {...props} />
  return (
    <React.Suspense fallback={<CodeLoading lines={18} className="px-4" />}>
      <LazyDiffList {...props} />
    </React.Suspense>
  )
}

export function CodeFile(props: CodeFileProps) {
  if (LoadedCodeFile) return <LoadedCodeFile {...props} />
  return (
    <React.Suspense fallback={<CodeLoading lines={18} />}>
      <LazyCodeFile {...props} />
    </React.Suspense>
  )
}

export function FileTree(props: FileTreeProps) {
  if (LoadedFileTree) return <LoadedFileTree {...props} />
  return (
    <React.Suspense fallback={<CodeLoading lines={8} className="px-3" />}>
      <LazyFileTree {...props} />
    </React.Suspense>
  )
}

function CodeLoading({
  lines,
  className,
}: {
  lines: number
  className?: string
}) {
  return (
    <div className={cn("flex flex-col gap-2 py-3", className)} aria-busy="true">
      {Array.from({ length: lines }, (_, line) => (
        <Skeleton
          key={line}
          className="h-3"
          style={{ width: `${35 + ((line * 37) % 50)}%` }}
        />
      ))}
    </div>
  )
}
