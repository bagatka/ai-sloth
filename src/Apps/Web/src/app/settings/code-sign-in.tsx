import { ArrowSquareOutIcon } from "@phosphor-icons/react"

import { buttonVariants } from "@/components/ui/button"
import { Spinner } from "@/components/ui/spinner"
import { seconds } from "../api"
import { CopyableCode } from "./section"

// Signing in at GitHub with a code, as connecting GitHub and adding a Copilot plan do: the host gets
// a code, the person enters it at GitHub and approves, and the page asks the host whether they have.

/** The code, the button that opens GitHub's page to enter it, and that the page is waiting. */
export function CodeSteps({ code, url }: { code: string; url: string }) {
  return (
    <>
      <CopyableCode code={code} />
      <a
        href={url}
        target="_blank"
        rel="noopener noreferrer"
        className={buttonVariants({ className: "w-full" })}
      >
        Open GitHub
        <ArrowSquareOutIcon data-icon="inline-end" />
      </a>
      <p
        role="status"
        className="flex items-center gap-2 text-label text-muted-foreground"
      >
        <Spinner className="size-3.5" />
        Waiting for you to approve it at GitHub
      </p>
    </>
  )
}

/**
 * Asks the host, with `complete`, whether the person approved the code, as often as GitHub allows,
 * until they have (its account), the code expires (null), or `signal` aborts.
 */
export async function waitForCode<T>(
  started: { interval: string; expiresAt: string },
  complete: () => Promise<{ account?: T | null; retryAfter: string }>,
  signal: AbortSignal
): Promise<T | null> {
  // GitHub says how often to ask; never more than once a second.
  let wait = Math.max(1, seconds(started.interval) ?? 5)
  while (Date.now() < Date.parse(started.expiresAt)) {
    await delay(wait * 1000, signal)
    const progress = await complete()
    if (progress.account) {
      return progress.account
    }
    wait = Math.max(1, seconds(progress.retryAfter) ?? wait)
  }
  return null
}

/** Whether waiting stopped because the dialog closed, rather than failed. */
export function aborted(error: unknown): boolean {
  return error instanceof DOMException && error.name === "AbortError"
}

function delay(milliseconds: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const timer = window.setTimeout(resolve, milliseconds)
    signal.addEventListener("abort", () => {
      window.clearTimeout(timer)
      reject(signal.reason)
    })
  })
}
