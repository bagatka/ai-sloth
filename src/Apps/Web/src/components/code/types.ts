import type * as React from "react"

/** How a diff lays out its two sides: one column of changes, or before and after side by side. */
export type DiffLayout = "unified" | "split"

/** One file's change: its text before and after, or its unified diff as git prints it. */
export type FileChange =
  | {
      /** The file's path, which also picks the language. */
      path: string
      before: string
      after: string
    }
  | {
      path: string
      patch: string
    }

/**
 * One change, inline in a conversation: no file header, long lines wrap, and a long diff starts
 * folded until asked.
 */
export type DiffProps = FileChange & {
  layout?: DiffLayout
  className?: string
}

/**
 * Many changes in one scroll, filling a panel or a page: each file's header stays at the top while
 * its diff is in view, long lines scroll sideways, and only the lines on screen are drawn.
 */
export type DiffListProps = {
  /** The changed files, in the order shown; `inTreeOrder` matches a tree beside them. Paths must be unique. */
  files: readonly FileChange[]
  layout?: DiffLayout
  /** Called with the files on screen, in order, whenever they change. */
  onFilesInView?: (paths: readonly string[]) => void
  ref?: React.Ref<DiffListHandle>
  className?: string
}

export type DiffListHandle = {
  /** Jumps to a file's diff, as far as the list scrolls, and briefly marks its header. */
  showFile: (path: string) => void
}

export type CodeFileProps = {
  path: string
  contents: string
  className?: string
}

export type FileChangeStatus = "added" | "modified" | "deleted" | "renamed"

export type FileTreeProps = {
  /** Every file's path, such as "src/orders/refund.ts". Pass a stable array: a new one resets the tree. */
  paths: readonly string[]
  /** Files that changed, marked in the tree. */
  changes?: readonly { path: string; status: FileChangeStatus }[]
  /**
   * The files shown as selected: a run of files, such as the ones on screen in a list beside the
   * tree. The tree marks them as one band, folders between them included, and keeps it in view.
   */
  selected?: readonly string[]
  /** Called when the reader picks a file. To show the pick, the caller changes `selected`. */
  onSelect?: (path: string) => void
  /** Show a search field above the tree. */
  search?: boolean
  className?: string
}
