import * as React from "react"
import { CheckIcon, CopyIcon, TrashIcon } from "@phosphor-icons/react"
import { cn } from "cn"

import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog"
import { Button } from "@/components/ui/button"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"

// How every part of the settings reads: a titled section that says what it's for, its things as
// rows, and its one way to add more at the top.

export function Section({
  id,
  title,
  description,
  action,
  children,
}: {
  id: string
  title: string
  description: React.ReactNode
  action?: React.ReactNode
  children: React.ReactNode
}) {
  return (
    <section
      id={id}
      aria-labelledby={`${id}-title`}
      className="flex scroll-mt-4 flex-col gap-3"
    >
      <div className="flex items-end gap-4">
        <div className="min-w-0 flex-1">
          <h2 id={`${id}-title`} className="text-title">
            {title}
          </h2>
          <p className="text-label text-pretty text-muted-foreground">
            {description}
          </p>
        </div>
        {action}
      </div>
      {children}
    </section>
  )
}

export function Rows({
  label,
  children,
}: {
  label: string
  children: React.ReactNode
}) {
  return (
    <ul
      aria-label={label}
      className="flex flex-col divide-y divide-border rounded-xl bg-card shadow-raised"
    >
      {children}
    </ul>
  )
}

export function Row({
  title,
  detail,
  children,
}: {
  title: React.ReactNode
  detail?: React.ReactNode
  /** What can be done with it, at the row's end. */
  children?: React.ReactNode
}) {
  return (
    <li className="flex min-h-14 items-center gap-3 px-4 py-2.5">
      <div className="min-w-0 flex-1">
        <div className="truncate text-sm font-medium">{title}</div>
        {detail && (
          <div className="truncate text-label text-muted-foreground">
            {detail}
          </div>
        )}
      </div>
      {children}
    </li>
  )
}

/** A row that says there's nothing yet, and what would be here. */
export function NoRows({ children }: { children: React.ReactNode }) {
  return (
    <li className="px-4 py-4 text-label text-muted-foreground">{children}</li>
  )
}

// Removing asks first, saying what removing does, since things using it stop.
export function RemoveButton({
  what,
  consequence,
  pending,
  onRemove,
}: {
  what: string
  consequence: string
  pending: boolean
  onRemove: () => void
}) {
  return (
    <AlertDialog>
      <Tooltip>
        <TooltipTrigger
          render={
            <AlertDialogTrigger
              render={
                <Button
                  variant="ghost"
                  size="icon-sm"
                  aria-label={`Remove ${what}`}
                  loading={pending}
                />
              }
            />
          }
        >
          <TrashIcon />
        </TooltipTrigger>
        <TooltipContent>Remove</TooltipContent>
      </Tooltip>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Remove {what}?</AlertDialogTitle>
          <AlertDialogDescription>{consequence}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Keep it</AlertDialogCancel>
          <AlertDialogAction variant="destructive" onClick={onRemove}>
            Remove
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}

/** A code to give someone or type elsewhere, large enough to read out, with a button that copies it. */
export function CopyableCode({
  code,
  className,
}: {
  code: string
  className?: string
}) {
  const [copied, setCopied] = React.useState(false)
  return (
    <div
      className={cn(
        "flex items-center gap-2 rounded-xl bg-muted px-4 py-3",
        className
      )}
    >
      <code className="min-w-0 flex-1 truncate bg-transparent p-0 font-mono text-lg tracking-wider">
        {code}
      </code>
      <Button
        variant="ghost"
        size="icon-sm"
        aria-label={copied ? "Copied" : "Copy"}
        onClick={() => {
          void navigator.clipboard.writeText(code).then(() => setCopied(true))
        }}
      >
        {copied ? <CheckIcon /> : <CopyIcon />}
      </Button>
    </div>
  )
}
