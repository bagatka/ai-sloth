import {
  File,
  Virtualizer,
  WorkerPoolContextProvider,
} from "@pierre/diffs/react"
import { cn } from "cn"

import { codeCSS, codeTheme, workerPool } from "./setup"
import type { CodeFileProps } from "./types"

// A file's contents, highlighted, with its header kept in view and only the lines on screen drawn.
export default function CodeFile({ path, contents, className }: CodeFileProps) {
  return (
    <WorkerPoolContextProvider {...workerPool}>
      <Virtualizer className={cn("size-full overflow-auto", className)}>
        <File
          file={{ name: path, contents }}
          options={{
            theme: codeTheme,
            overflow: "scroll",
            stickyHeader: true,
            unsafeCSS: codeCSS,
          }}
        />
      </Virtualizer>
    </WorkerPoolContextProvider>
  )
}
