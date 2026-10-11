import type { ReactNode } from "react"
import {
  CheckCircleIcon,
  CircleNotchIcon,
  WarningCircleIcon,
} from "@phosphor-icons/react"

import { cn } from "cn"

import { Alert, AlertDescription } from "@/components/ui/alert"

// What sits on a chat's message box while it matters, where people look next: the nook's setup, a
// plan near its limit. Each is one line at the message box's width.

/** One line: what's happening, and what to do about it at its end. With buttons or without, it's the same height, so it doesn't jump as one comes or goes. */
export function BoxLine({
  tone,
  children,
  actions,
}: {
  tone: "working" | "done" | "warning"
  children: string
  actions?: ReactNode
}) {
  return (
    <BoxBanner>
      <Alert
        className={cn(
          "flex flex-wrap items-center gap-x-3 gap-y-2 *:[svg]:translate-y-0",
          actions ? "py-2" : "py-3",
          tone === "done" && "*:[svg]:text-primary",
          tone === "warning" && "*:[svg]:text-warning"
        )}
      >
        {tone === "done" ? (
          <CheckCircleIcon />
        ) : tone === "warning" ? (
          <WarningCircleIcon />
        ) : (
          <CircleNotchIcon className="animate-spin motion-reduce:animate-none" />
        )}
        <AlertDescription className="min-w-0 flex-1 text-foreground">
          {children}
        </AlertDescription>
        {actions && (
          <div className="flex shrink-0 items-center gap-1.5">{actions}</div>
        )}
      </Alert>
    </BoxBanner>
  )
}

/** Something on the message box, at its width. */
export function BoxBanner({ children }: { children: ReactNode }) {
  return (
    <div className="shrink-0 px-4 pb-2 md:px-5">
      <div className="mx-auto w-full max-w-[44rem]">{children}</div>
    </div>
  )
}
