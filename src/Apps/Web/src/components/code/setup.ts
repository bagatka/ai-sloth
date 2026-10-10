import { registerCustomCSSVariableTheme } from "@pierre/diffs"
import { getOrCreateWorkerPoolSingleton } from "@pierre/diffs/worker"
import HighlightWorker from "@pierre/diffs/worker/worker.js?worker"

// Syntax colors come from the --diffs-token-* variables in src/index.css, so code follows the
// light and dark themes without being highlighted again.
export const codeTheme = "aisloth"
registerCustomCSSVariableTheme(codeTheme, {
  foreground: "currentColor",
  background: "transparent",
})

// Code views and the tree draw in shadow DOM, which the app's own selection and scrollbar styles
// don't reach; these bring them in. Diff headers show counts through ChangeCounts instead of their own.
const shadowBasics = `
  ::selection { background: color-mix(in oklch, var(--primary) 28%, transparent); }
  * { scrollbar-width: thin; scrollbar-color: color-mix(in oklch, var(--foreground) 20%, transparent) transparent; }
`
export const codeCSS = `${shadowBasics}
  [data-additions-count], [data-deletions-count] { display: none; }
`
// The tree rings the row it last focused even after a click; like everywhere else in the app, the
// ring shows only for keyboard focus.
export const treeCSS = `${shadowBasics}
  [data-type="item"]:not(:focus-visible)::before { outline-color: transparent; }
`

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
