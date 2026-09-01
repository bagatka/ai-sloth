import { useCallback, useEffect, useRef, useState } from "react"
import type { AuthenticatedRequest } from "@/authentication"
import type { Workspace, WorkspaceOutcome, Workspaces } from ".."
import { createWorkspace, joinWorkspace, listWorkspaces } from "./api"

type WorkspaceOperation = (
  signal: AbortSignal
) => Promise<{ ok: true; value: Workspace } | { ok: false; error: string }>

export function useWorkspaces(request: AuthenticatedRequest): Workspaces {
  const loadRequest = useRef<AbortController | null>(null)
  const activeOperation = useRef<AbortController | null>(null)
  const mounted = useRef(false)
  const [status, setStatus] = useState<Workspaces["status"]>("loading")
  const [workspaces, setWorkspaces] = useState<Workspace[]>([])
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState<Workspaces["pending"]>(null)

  const load = useCallback(() => {
    loadRequest.current?.abort()
    const controller = new AbortController()
    loadRequest.current = controller

    void listWorkspaces(request, controller.signal)
      .then((outcome) => {
        if (loadRequest.current !== controller || !mounted.current) return
        if (outcome.ok) {
          setWorkspaces(outcome.value)
          setStatus("ready")
        } else {
          setError(outcome.error)
          setStatus("error")
        }
      })
      .catch(() => {
        if (loadRequest.current !== controller || !mounted.current) return
        setError(
          controller.signal.aborted
            ? "Workspace loading was canceled"
            : "Could not reach the workspace service"
        )
        setStatus("error")
      })
  }, [request])

  const reload = useCallback(() => {
    setStatus("loading")
    setError(null)
    load()
  }, [load])

  const perform = useCallback(
    async (
      kind: Exclude<Workspaces["pending"], null>,
      operation: WorkspaceOperation
    ): Promise<WorkspaceOutcome> => {
      activeOperation.current?.abort()
      const controller = new AbortController()
      activeOperation.current = controller
      if (mounted.current) setPending(kind)

      try {
        const outcome = await operation(controller.signal)
        if (
          outcome.ok &&
          activeOperation.current === controller &&
          mounted.current
        ) {
          setWorkspaces((current) =>
            current.some((workspace) => workspace.id === outcome.value.id)
              ? current
              : [...current, outcome.value]
          )
        }
        return outcome
      } catch {
        return {
          ok: false,
          error: controller.signal.aborted
            ? "Workspace operation was canceled"
            : "Could not reach the workspace service",
        }
      } finally {
        if (activeOperation.current === controller) {
          activeOperation.current = null
          if (mounted.current) setPending(null)
        }
      }
    },
    []
  )

  const create = useCallback(
    (name: string) =>
      perform("create", (signal) => createWorkspace(request, name, signal)),
    [perform, request]
  )

  const join = useCallback(
    (invitationCode: string) =>
      perform("join", (signal) =>
        joinWorkspace(request, invitationCode, signal)
      ),
    [perform, request]
  )

  useEffect(() => {
    mounted.current = true
    load()
    return () => {
      mounted.current = false
      loadRequest.current?.abort()
      activeOperation.current?.abort()
    }
  }, [load])

  return {
    status,
    workspaces,
    error,
    pending,
    create,
    join,
    reload,
  }
}
