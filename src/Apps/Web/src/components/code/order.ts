// Files and folders in the order the tree shows them: folders before files, then names in natural
// order ("item2" before "item10"), the same on every machine. A list beside a tree follows it too.
const nameOrder = new Intl.Collator("en", {
  numeric: true,
  sensitivity: "base",
})

type PathEntry = { segments: readonly string[]; isDirectory: boolean }

export function comparePaths(left: PathEntry, right: PathEntry): number {
  const depth = Math.min(left.segments.length, right.segments.length)
  for (let index = 0; index < depth; index++) {
    const leftName = left.segments[index]
    const rightName = right.segments[index]
    if (leftName === rightName) continue
    const leftIsFolder = index < left.segments.length - 1 || left.isDirectory
    const rightIsFolder = index < right.segments.length - 1 || right.isDirectory
    if (leftIsFolder !== rightIsFolder) return leftIsFolder ? -1 : 1
    return (
      nameOrder.compare(leftName, rightName) || (leftName < rightName ? -1 : 1)
    )
  }
  return left.segments.length - right.segments.length
}

export function compareFilePaths(left: string, right: string): number {
  return comparePaths(
    { segments: left.split("/"), isDirectory: false },
    { segments: right.split("/"), isDirectory: false }
  )
}

/** Files in the order a FileTree shows them, for a list that sits beside one. */
export function inTreeOrder<File extends { path: string }>(
  files: readonly File[]
): File[] {
  return [...files].sort((left, right) =>
    compareFilePaths(left.path, right.path)
  )
}
