import * as React from "react"

import { modPressed } from "@/components/shortcut"

// A keyboard shortcut listens on the window for as long as the component that declares it is on
// screen. Subscribing to the DOM is what effects are for, so this file is allowed one.
export function useShortcut(
  shortcut: { key: string; mod?: boolean },
  action: () => void
) {
  const run = React.useEffectEvent(action)
  const { key, mod = false } = shortcut

  React.useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key.toLowerCase() !== key.toLowerCase()) return
      if (modPressed(event) !== mod || event.altKey || event.shiftKey) return
      event.preventDefault()
      run()
    }
    window.addEventListener("keydown", onKeyDown)
    return () => window.removeEventListener("keydown", onKeyDown)
  }, [key, mod])
}
