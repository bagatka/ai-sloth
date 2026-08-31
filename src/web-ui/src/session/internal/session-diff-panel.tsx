import { lazy, Suspense, useEffect, useState } from "react"
import { XIcon } from "lucide-react"
import { Button } from "@/components/ui/button"
import type { AuthenticatedRequest } from ".."
import { getSessionDiff } from "./api"
import { isUnifiedPatch } from "./patch"
import type { WorkingDiffState } from "./use-working-diff"

const InlineDiff = lazy(() =>
  import("./inline-diff").then((module) => ({ default: module.InlineDiff }))
)

type SessionDiffPanelProps = {
  request: AuthenticatedRequest
  workspaceId: string
  sessionId: string
  onClose(): void
  source:
    | { type: "revision"; revision: number }
    | {
        type: "working"
        turnId: string
        sequence: number | null
        updating: boolean
        state: WorkingDiffState
        onRetry(): void
      }
}

export function SessionDiffPanel(props: SessionDiffPanelProps) {
  return props.source.type === "working" ? (
    <WorkingSessionDiffPanel {...props} source={props.source} />
  ) : (
    <RevisionSessionDiffPanel {...props} revision={props.source.revision} />
  )
}

function WorkingSessionDiffPanel({
  source,
  onClose,
}: SessionDiffPanelProps & {
  source: Extract<SessionDiffPanelProps["source"], { type: "working" }>
}) {
  const current =
    source.state.status !== "idle" &&
    source.state.turnId === source.turnId &&
    source.state.sequence === source.sequence
      ? source.state
      : null
  const state: DiffState =
    source.updating || current === null || current.status === "loading"
      ? { status: "loading" }
      : current.status === "ready"
        ? { status: "ready", patch: current.patch }
        : { status: "error", message: current.message }

  return (
    <DiffPanel
      subtitle="Session start → Live working tree"
      state={state}
      renderVersion={source.sequence ?? "updating"}
      onClose={onClose}
      onRetry={source.onRetry}
    />
  )
}

function RevisionSessionDiffPanel({
  request,
  workspaceId,
  sessionId,
  revision,
  onClose,
}: Omit<SessionDiffPanelProps, "source"> & { revision: number }) {
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
    <DiffPanel
      subtitle={`Session start → Revision ${revision}`}
      state={state}
      renderVersion={revision}
      onClose={onClose}
      onRetry={() => {
        setState({ status: "loading" })
        setGeneration((value) => value + 1)
      }}
    />
  )
}

function DiffPanel({
  subtitle,
  state,
  renderVersion,
  onClose,
  onRetry,
}: {
  subtitle: string
  state: DiffState
  renderVersion: number | string
  onClose(): void
  onRetry(): void
}) {
  return (
    <aside
      aria-label="Complete session diff"
      className="absolute inset-y-0 right-0 z-10 flex w-full flex-col border-l bg-background md:static md:w-[min(48rem,48vw)]"
    >
      <header className="flex h-12 shrink-0 items-center gap-3 border-b px-3">
        <div className="min-w-0 flex-1">
          <h2 className="text-sm font-medium">Session changes</h2>
          <p className="text-xs text-muted-foreground">{subtitle}</p>
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
      <div className="min-h-0 flex-1 overflow-auto p-3">
        {state.status === "loading" ? (
          <p className="text-sm text-muted-foreground">Updating changes…</p>
        ) : state.status === "error" ? (
          <div className="space-y-3 text-sm">
            <p className="text-destructive">{state.message}</p>
            <Button type="button" size="sm" variant="outline" onClick={onRetry}>
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
            <InlineDiff key={renderVersion} patch={state.patch} virtualized />
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
