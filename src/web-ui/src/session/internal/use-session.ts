import { useCallback, useEffect, useRef, useState } from "react"
import type { AuthenticatedRequest, SessionDetails, SessionEvent } from ".."
import {
  continueSession,
  getSessionDetails,
  publishSession,
  readSessionEvents,
} from "./api"
import { reduceSessionEvent, type SessionTranscript } from "./transcript"

const RETAINED_TURNS = 10

export function useSession(
  request: AuthenticatedRequest,
  workspaceId: string,
  sessionId: string
) {
  const activePrompt = useRef<AbortController | null>(null)
  const activePublication = useRef<AbortController | null>(null)
  const mounted = useRef(false)
  const [generation, setGeneration] = useState(0)
  const [details, setDetails] = useState<SessionDetails | null>(null)
  const [events, setEvents] = useState<SessionTranscript>(new Map())
  const [status, setStatus] = useState<"loading" | "ready" | "error">("loading")
  const [pending, setPending] = useState(false)
  const [publishing, setPublishing] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const accept = useCallback((event: SessionEvent) => {
    if (!mounted.current) return
    setEvents((current) => reduceSessionEvent(current, event))
  }, [])

  const reload = useCallback(() => {
    setStatus("loading")
    setError(null)
    setGeneration((current) => current + 1)
  }, [])

  const send = useCallback(
    async (prompt: string): Promise<boolean> => {
      if (pending || publishing) return false
      activePrompt.current?.abort()
      const controller = new AbortController()
      activePrompt.current = controller
      setPending(true)
      setError(null)
      try {
        await continueSession(
          request,
          workspaceId,
          sessionId,
          prompt,
          controller.signal
        )
        if (mounted.current) reload()
        return true
      } catch (cause) {
        if (mounted.current) {
          setError(
            cause instanceof Error
              ? cause.message
              : "Could not continue session"
          )
        }
        return false
      } finally {
        if (activePrompt.current === controller) {
          activePrompt.current = null
          if (mounted.current) setPending(false)
        }
      }
    },
    [pending, publishing, reload, request, sessionId, workspaceId]
  )

  const publish = useCallback(async (): Promise<boolean> => {
    if (pending || publishing) return false
    activePublication.current?.abort()
    const controller = new AbortController()
    activePublication.current = controller
    setPublishing(true)
    setError(null)
    try {
      const publication = await publishSession(
        request,
        workspaceId,
        sessionId,
        controller.signal
      )
      if (mounted.current) {
        setDetails((current) => current && { ...current, publication })
      }
      return true
    } catch (cause) {
      if (mounted.current) {
        setError(
          cause instanceof Error ? cause.message : "Could not publish session"
        )
      }
      return false
    } finally {
      if (activePublication.current === controller) {
        activePublication.current = null
        if (mounted.current) setPublishing(false)
      }
    }
  }, [pending, publishing, request, sessionId, workspaceId])

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
      activePrompt.current?.abort()
      activePublication.current?.abort()
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()

    void getSessionDetails(request, workspaceId, sessionId, controller.signal)
      .then(async (value) => {
        if (controller.signal.aborted || !mounted.current) return
        setDetails(value)
        setEvents(new Map())
        setStatus("ready")

        const turns = value.turns.slice(-RETAINED_TURNS)
        const activeTurn = turns.at(-1)
        const completed =
          activeTurn &&
          (activeTurn.status === "running" ||
            activeTurn.status === "finalizing")
            ? turns.slice(0, -1)
            : turns
        await Promise.all(
          completed.map((turn) =>
            readSessionEvents(
              request,
              workspaceId,
              sessionId,
              turn.id,
              0,
              false,
              controller.signal,
              accept
            )
          )
        )
        if (
          activeTurn &&
          (activeTurn.status === "running" ||
            activeTurn.status === "finalizing") &&
          !controller.signal.aborted
        ) {
          await readSessionEvents(
            request,
            workspaceId,
            sessionId,
            activeTurn.id,
            0,
            true,
            controller.signal,
            accept
          )
          if (!controller.signal.aborted && mounted.current) reload()
        }
      })
      .catch((cause) => {
        if (controller.signal.aborted || !mounted.current) return
        setError(
          cause instanceof Error ? cause.message : "Could not load session"
        )
        setStatus("error")
      })

    return () => controller.abort()
  }, [accept, generation, reload, request, sessionId, workspaceId])

  return {
    details,
    events,
    status,
    pending,
    publishing,
    error,
    reload,
    send,
    publish,
  }
}
