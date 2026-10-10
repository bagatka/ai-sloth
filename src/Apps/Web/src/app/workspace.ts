import { useSyncExternalStore } from "react"

// Which workspace's chats the sidebar lists. The browser remembers the last one, as a convenience:
// without storage it starts from the person's first workspace.
const key = "workspace"
const listeners = new Set<() => void>()

function read(): string | null {
  try {
    return localStorage.getItem(key)
  } catch {
    return null
  }
}

let chosen = read()

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

/** The workspace chosen last in this browser, if any. */
export function useChosenWorkspace(): string | null {
  return useSyncExternalStore(subscribe, () => chosen)
}

export function chooseWorkspace(id: string) {
  if (chosen === id) {
    return
  }
  chosen = id
  try {
    localStorage.setItem(key, id)
  } catch {
    // The browser keeps nothing; the choice lasts while the page is open.
  }
  for (const listener of listeners) {
    listener()
  }
}
