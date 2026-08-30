import { useMemo } from "react"
import type { CodeViewItem, FileDiffOptions } from "@pierre/diffs"
import {
  CodeView,
  FileDiff,
  type CodeViewReactOptions,
} from "@pierre/diffs/react"
import { useTheme } from "@/hooks/use-theme"
import { parseFiles } from "./parse-diff"

export function InlineDiff({
  patch,
  virtualized = false,
}: {
  patch: string
  virtualized?: boolean
}) {
  const { theme } = useTheme()
  const files = useMemo(() => parseFiles(patch), [patch])
  const options = useMemo(
    () =>
      ({
        theme: { dark: "pierre-dark", light: "pierre-light" },
        themeType: theme,
        diffStyle: "unified",
        diffIndicators: "classic",
        hunkSeparators: "metadata",
        lineDiffType: "word",
      }) satisfies FileDiffOptions<undefined>,
    [theme]
  )

  const items = useMemo<CodeViewItem[]>(
    () =>
      files.map((file, index) => ({
        id: `${file.cacheKey ?? file.name}-${index}`,
        type: "diff",
        fileDiff: file,
        version: 0,
      })),
    [files]
  )
  const codeViewOptions = useMemo<CodeViewReactOptions<undefined>>(
    () => ({
      ...options,
      stickyHeaders: true,
      layout: { paddingTop: 0, paddingBottom: 12, gap: 12 },
    }),
    [options]
  )

  if (files.length === 0) {
    return <pre className="text-xs whitespace-pre-wrap">{patch}</pre>
  }
  if (virtualized && files.length > 1) {
    return (
      <CodeView
        items={items}
        options={codeViewOptions}
        style={{ height: "100%", overflow: "auto" }}
      />
    )
  }
  return (
    <div className="space-y-3">
      {files.map((file, index) => (
        <FileDiff
          key={`${file.cacheKey ?? file.name}-${index}`}
          fileDiff={file}
          options={options}
        />
      ))}
    </div>
  )
}
