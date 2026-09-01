import { useCallback, useEffect, useState } from "react"
import type { AuthenticatedRequest } from ".."
import { getSessionWorkingDiff } from "./api"

export type WorkingDiffState =
  | { status: "idle" }
  | { status: "loading"; turnId: string; sequence: number }
  | { status: "ready"; turnId: string; sequence: number; patch: string }
  | {
      status: "error"
      turnId: string
      sequence: number
      message: string
    }

export function useWorkingDiff(
  request: AuthenticatedRequest,
  workspaceId: string,
  sessionId: string,
  turnId: string | null,
  sequence: number | null,
  enabled: boolean
): { state: WorkingDiffState; retry(): void } {
  const [generation, setGeneration] = useState(0)
  const [state, setState] = useState<WorkingDiffState>({ status: "idle" })
  const retry = useCallback(() => {
    if (turnId !== null && sequence !== null) {
      setState({ status: "loading", turnId, sequence })
      setGeneration((value) => value + 1)
    }
  }, [sequence, turnId])

  useEffect(() => {
    if (!enabled || turnId === null || sequence === null) return

    const controller = new AbortController()
    void getSessionWorkingDiff(
      request,
      workspaceId,
      sessionId,
      turnId,
      controller.signal
    )
      .then((patch) => {
        if (!controller.signal.aborted) {
          setState({ status: "ready", turnId, sequence, patch })
        }
      })
      .catch((cause) => {
        if (controller.signal.aborted) return
        setState({
          status: "error",
          turnId,
          sequence,
          message:
            cause instanceof Error
              ? cause.message
              : "Could not load live working diff",
        })
      })

    return () => controller.abort()
  }, [enabled, generation, request, sequence, sessionId, turnId, workspaceId])

  const current =
    turnId !== null &&
    sequence !== null &&
    (state.status === "idle" ||
      state.turnId !== turnId ||
      state.sequence !== sequence)
      ? ({ status: "loading", turnId, sequence } as const)
      : state

  return { state: current, retry }
}
