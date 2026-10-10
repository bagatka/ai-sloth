import type * as React from "react"
import { ArrowFatUpIcon, CommandIcon, OptionIcon } from "@phosphor-icons/react"

import { Kbd, KbdGroup } from "@/components/ui/kbd"

// "Mod" is ⌘ on Apple keyboards and Ctrl everywhere else. The platform can't change while the page
// is open, so it's read once.
const apple = /Mac|iPhone|iPad/.test(
  (navigator as Navigator & { userAgentData?: { platform: string } })
    .userAgentData?.platform ?? navigator.platform
)

// Whether a key event has this keyboard's Mod key down.
export function modPressed(event: KeyboardEvent | React.KeyboardEvent) {
  return apple ? event.metaKey : event.ctrlKey
}

type Key = "Mod" | "Shift" | "Alt" | (string & {})

// A keyboard shortcut as this person's keyboard labels it: as key caps, or plain inside menus.
export function Shortcut({
  keys,
  plain = false,
}: {
  keys: Key[]
  plain?: boolean
}) {
  if (plain) {
    return (
      <span
        className={
          apple
            ? "inline-flex items-center gap-px"
            : "inline-flex items-center gap-1"
        }
      >
        {keys.map((key) => (
          // Each key in its own element: the browser merges adjacent text into one flex item.
          <span key={key}>
            <KeyLabel name={key} />
          </span>
        ))}
      </span>
    )
  }

  return (
    <KbdGroup>
      {keys.map((key) => (
        <Kbd key={key}>
          <KeyLabel name={key} />
        </Kbd>
      ))}
    </KbdGroup>
  )
}

function KeyLabel({ name }: { name: Key }) {
  if (name === "Mod") {
    return apple ? <CommandIcon role="img" aria-label="Command" /> : "Ctrl"
  }

  if (name === "Shift") {
    return apple ? <ArrowFatUpIcon role="img" aria-label="Shift" /> : "Shift"
  }

  if (name === "Alt") {
    return apple ? <OptionIcon role="img" aria-label="Option" /> : "Alt"
  }

  return name
}
