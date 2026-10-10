import {
  File,
  Virtualizer,
  WorkerPoolContextProvider,
} from "@pierre/diffs/react"
import { cn } from "cn"

import { codeCSS, codeTheme, workerPool } from "./setup"
import type { CodeFileProps } from "./types"

// A file's contents, highlighted, with its header kept in view and only the lines on screen drawn.
// The code would paint over the scroller's own focus outline, so a wrapper lays the ring on top.
export default function CodeFile({ path, contents, className }: CodeFileProps) {
  return (
    <WorkerPoolContextProvider {...workerPool}>
      <div
        className={cn("focus-ring-over size-full rounded-[inherit]", className)}
      >
        <Virtualizer className="size-full overflow-auto rounded-[inherit] outline-none">
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
      </div>
    </WorkerPoolContextProvider>
  )
}
