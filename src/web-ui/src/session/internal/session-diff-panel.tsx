import { lazy, Suspense, useEffect, useState } from "react"
import { XIcon } from "lucide-react"
import { Button } from "@/components/ui/button"
import type { AuthenticatedRequest } from ".."
import { getSessionDiff } from "./api"
import { isUnifiedPatch } from "./patch"

const InlineDiff = lazy(() =>
  import("./inline-diff").then((module) => ({ default: module.InlineDiff }))
)

export function SessionDiffPanel({
  request,
  workspaceId,
  sessionId,
  revision,
  onClose,
}: {
  request: AuthenticatedRequest
  workspaceId: string
  sessionId: string
  revision: number
  onClose(): void
}) {
  const [generation, setGeneration] = useState(0)
  const [state, setState] = useState<DiffState>({ status: "loading" })

  useEffect(() => {
    const controller = new AbortController()
    void getSessionDiff(request, workspaceId, sessionId, controller.signal)
      .then((result) => {
        if (controller.signal.aborted) return
        setState(
          result.revision === revision
            ? { status: "ready", patch: result.patch }
            : {
                status: "error",
                message: "The session revision changed. Retry.",
              }
        )
      })
      .catch((cause) => {
        if (controller.signal.aborted) return
        setState({
          status: "error",
          message:
            cause instanceof Error
              ? cause.message
              : "Could not load session diff",
        })
      })
    return () => controller.abort()
  }, [generation, request, revision, sessionId, workspaceId])

  return (
    <aside
      aria-label="Complete session diff"
      className="absolute inset-y-0 right-0 z-10 flex w-full flex-col border-l bg-background md:static md:w-[min(48rem,48vw)]"
    >
      <header className="flex h-12 shrink-0 items-center gap-3 border-b px-3">
        <div className="min-w-0 flex-1">
          <h2 className="text-sm font-medium">Session changes</h2>
          <p className="text-xs text-muted-foreground">
            Session start → Revision {revision}
          </p>
        </div>
        <Button
          type="button"
          size="icon-sm"
          variant="ghost"
          title="Close session changes"
          onClick={onClose}
        >
          <XIcon />
        </Button>
      </header>
      <div className="min-h-0 flex-1 overflow-hidden p-3">
        {state.status === "loading" ? (
          <p className="text-sm text-muted-foreground">Loading changes…</p>
        ) : state.status === "error" ? (
          <div className="space-y-3 text-sm">
            <p className="text-destructive">{state.message}</p>
            <Button
              type="button"
              size="sm"
              variant="outline"
              onClick={() => {
                setState({ status: "loading" })
                setGeneration((value) => value + 1)
              }}
            >
              Retry
            </Button>
          </div>
        ) : state.patch.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            No repository changes in this session.
          </p>
        ) : isUnifiedPatch(state.patch) ? (
          <Suspense
            fallback={
              <p className="text-sm text-muted-foreground">
                Rendering changes…
              </p>
            }
          >
            <InlineDiff patch={state.patch} virtualized />
          </Suspense>
        ) : (
          <pre className="max-h-full overflow-auto text-xs whitespace-pre-wrap">
            {state.patch}
          </pre>
        )}
      </div>
    </aside>
  )
}

type DiffState =
  | { status: "loading" }
  | { status: "ready"; patch: string }
  | { status: "error"; message: string }
