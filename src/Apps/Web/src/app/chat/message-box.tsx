import * as React from "react"

// Where people write to an agent, in a chat or starting one: Enter sends, and Shift+Enter, or Enter
// while an input method composes, adds a line. Controls sit along its bottom edge, its action at
// the end.
export function MessageBox({
  value,
  onValueChange,
  onSubmit,
  onEscape,
  onFocus,
  placeholder,
  autoFocus,
  controls,
  action,
}: {
  value: string
  onValueChange: (value: string) => void
  onSubmit: () => void
  /** Esc in the box; it does nothing otherwise. */
  onEscape?: () => void
  onFocus?: () => void
  placeholder: string
  autoFocus?: boolean
  controls?: React.ReactNode
  action: React.ReactNode
}) {
  return (
    <form
      className="mx-auto w-full max-w-[44rem] rounded-2xl border focus-field-within border-input bg-card shadow-field transition-[border-color,box-shadow]"
      onSubmit={(event) => {
        event.preventDefault()
        onSubmit()
      }}
    >
      <textarea
        aria-label="Message"
        value={value}
        onChange={(event) => onValueChange(event.target.value)}
        onFocus={onFocus}
        autoFocus={autoFocus}
        onKeyDown={(event) => {
          if (event.key === "Escape" && onEscape) {
            event.preventDefault()
            onEscape()
            return
          }
          if (
            event.key !== "Enter" ||
            event.shiftKey ||
            event.nativeEvent.isComposing
          ) {
            return
          }
          event.preventDefault()
          onSubmit()
        }}
        placeholder={placeholder}
        rows={2}
        className="block [field-sizing:content] max-h-60 min-h-16 w-full resize-none bg-transparent px-[15px] pt-3.5 pb-1 text-base outline-none md:text-read"
      />
      <div className="flex items-center gap-1 px-2 pb-2">
        <div className="flex min-w-0 flex-1 items-center gap-1">{controls}</div>
        {action}
      </div>
    </form>
  )
}
