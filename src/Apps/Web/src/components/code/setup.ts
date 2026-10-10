import {
  getSingularPatch,
  parseDiffFromFile,
  registerCustomCSSVariableTheme,
  type FileDiffMetadata,
} from "@pierre/diffs"
import { getOrCreateWorkerPoolSingleton } from "@pierre/diffs/worker"
import HighlightWorker from "@pierre/diffs/worker/worker.js?worker"

import type { FileChange } from "./types"

// Syntax colors come from the --diffs-token-* variables in src/index.css, so code follows the
// light and dark themes without being highlighted again.
export const codeTheme = "aisloth"
registerCustomCSSVariableTheme(codeTheme, {
  foreground: "currentColor",
  background: "transparent",
})

// Code views and the tree draw in shadow DOM, which the app's own selection and scrollbar styles
// don't reach; these bring them in.
const shadowBasics = `
  ::selection { background: color-mix(in oklch, var(--primary) 28%, transparent); }
  * { scrollbar-width: thin; scrollbar-color: color-mix(in oklch, var(--foreground) 20%, transparent) transparent; }
`
// File headers show counts through ChangeCounts instead of their own. They get a hairline above and
// below: between files in a list, and under a header kept at the top while code scrolls beneath it.
// The line above falls outside the view for the first file.
export const codeCSS = `${shadowBasics}
  [data-additions-count], [data-deletions-count] { display: none; }
  [data-diffs-header] { box-shadow: 0 -1px 0 var(--border), 0 1px 0 var(--border); }
`
// The tree rings the row it last focused even after a click; like everywhere else in the app, the
// ring shows only for keyboard focus. Selected rows next to each other read as one band.
export const treeCSS = `${shadowBasics}
  [data-type="item"]:not(:focus-visible)::before { outline-color: transparent; }
  [data-item-selected="true"]:has(+ [data-item-selected="true"]) { border-end-start-radius: 0; border-end-end-radius: 0; }
  [data-item-selected="true"] + [data-item-selected="true"] { border-start-start-radius: 0; border-start-end-radius: 0; }
`

// How every diff reads: changed lines marked with a bar, changed words tinted, and the unchanged
// lines between changes folded into a line that says how many.
export const diffOptions = {
  theme: codeTheme,
  diffIndicators: "bars",
  lineDiffType: "word-alt",
  // Not handled: expanding unchanged lines from the keyboard. The library draws its expander as
  // a div that can't take focus; fixing that needs a change in @pierre/diffs itself.
  hunkSeparators: "line-info-basic",
  unsafeCSS: codeCSS,
} as const

export function parseChange(change: FileChange): FileDiffMetadata {
  if ("patch" in change) return getSingularPatch(change.patch)
  return parseDiffFromFile(
    { name: change.path, contents: change.before },
    { name: change.path, contents: change.after }
  )
}

// Highlighting runs in a small pool of workers, off the main thread. The pool is created with the
// first code view and lives as long as the page; more than a few workers only costs memory.
export const workerPool = {
  poolOptions: {
    workerFactory: () => new HighlightWorker(),
    poolSize: Math.max(
      1,
      Math.min(3, (navigator.hardwareConcurrency || 2) - 1)
    ),
  },
  highlighterOptions: { theme: codeTheme },
}

// Starts the workers ahead of the first code view, so it shows at once instead of waiting for them.
export function startHighlighting() {
  getOrCreateWorkerPoolSingleton(workerPool)
}
