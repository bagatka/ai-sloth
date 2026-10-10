import type { FileChangeStatus } from "@/components/code"

// A repository's changes as the host sends them, one patch as `git diff` prints it, split into its
// files: each file's section starts with "diff --git", and says how the file changed in the lines
// before its first hunk.

export type ChangedFile = {
  path: string
  status: FileChangeStatus
  additions: number
  deletions: number
  /** The file's own section of the patch. */
  patch: string
}

export function changedFiles(patch: string): ChangedFile[] {
  const sections = patch
    .split(/^(?=diff --git )/m)
    .filter((section) => section.startsWith("diff --git "))
  return sections.map(fileOf)
}

function fileOf(section: string): ChangedFile {
  const lines = section.split("\n")
  const hunk = lines.findIndex((line) => line.startsWith("@@"))
  const header = hunk < 0 ? lines : lines.slice(0, hunk)
  const body = hunk < 0 ? [] : lines.slice(hunk)
  const after = valueOf(header, "+++ b/")
  const before = valueOf(header, "--- a/")
  const renamed = valueOf(header, "rename to ")
  const status: FileChangeStatus = header.some((line) =>
    line.startsWith("new file mode")
  )
    ? "added"
    : header.some((line) => line.startsWith("deleted file mode"))
      ? "deleted"
      : renamed !== null
        ? "renamed"
        : "modified"
  return {
    // A binary file's section has no ---/+++ lines; its name is in the first line, which is
    // "diff --git a/<path> b/<path>" for a file that kept its name.
    // Not handled: names git quotes, those with quotes, backslashes, or control characters.
    path: renamed ?? after ?? before ?? sameName(lines[0]!),
    status,
    additions: body.filter((line) => line.startsWith("+")).length,
    deletions: body.filter((line) => line.startsWith("-")).length,
    patch: section,
  }
}

function valueOf(lines: readonly string[], prefix: string): string | null {
  const line = lines.find((found) => found.startsWith(prefix))
  return line === undefined ? null : line.slice(prefix.length)
}

// "diff --git a/<path> b/<path>": both halves are the same path, so it's the second half.
function sameName(first: string): string {
  const both = first.slice("diff --git a/".length)
  return both.slice((both.length - "b/".length - 1) / 2 + " b/".length)
}
