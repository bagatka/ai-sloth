/** How a diff lays out its two sides: one column of changes, or before and after side by side. */
export type DiffLayout = "unified" | "split"

/**
 * Where a diff sits. Inline, in a conversation: no file header, long lines wrap, and a long diff
 * starts folded. Panel, filling a side panel or a page: the file header stays at the top, long lines
 * scroll sideways, and only the lines on screen are drawn.
 */
export type DiffVariant = "inline" | "panel"

export type DiffProps = (
  | {
      /** The file's path, which also picks the language. */
      path: string
      before: string
      after: string
    }
  | {
      /** One file's unified diff, as git prints it. */
      patch: string
    }
) & {
  variant?: DiffVariant
  layout?: DiffLayout
  className?: string
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
  selected?: string
  onSelect?: (path: string) => void
  /** Show a search field above the tree. */
  search?: boolean
  className?: string
}
