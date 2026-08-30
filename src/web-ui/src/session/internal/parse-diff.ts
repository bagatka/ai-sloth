import { parsePatchFiles, type FileDiffMetadata } from "@pierre/diffs"

export function parseFiles(patch: string): FileDiffMetadata[] {
  try {
    return parsePatchFiles(patch, undefined, true).flatMap(
      (parsed) => parsed.files
    )
  } catch {
    return []
  }
}
