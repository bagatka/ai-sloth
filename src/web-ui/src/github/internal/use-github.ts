import { useCallback, useEffect, useRef, useState } from "react"
import type { GitHub, GitHubRequest } from ".."
import {
  disconnectGitHub,
  getGitHubConnection,
  startGitHubConnection,
} from "./api"

export function useGitHub(request: GitHubRequest): GitHub {
  const active = useRef<AbortController | null>(null)
  const mounted = useRef(false)
  const [status, setStatus] = useState<GitHub["status"]>("loading")
  const [connection, setConnection] = useState<GitHub["connection"]>(null)
  const [installationUrl, setInstallationUrl] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    active.current?.abort()
    const controller = new AbortController()
    active.current = controller

    void getGitHubConnection(request, controller.signal)
      .then((value) => {
        if (!mounted.current || active.current !== controller) return
        setConnection(value.connection)
        setInstallationUrl(value.installationUrl)
        setStatus("ready")
      })
      .catch((cause) => {
        if (!mounted.current || active.current !== controller) return
        setError(
          cause instanceof Error ? cause.message : "Could not load GitHub"
        )
        setStatus("error")
      })
      .finally(() => {
        if (active.current === controller) active.current = null
      })
  }, [request])

  const reload = useCallback(() => {
    setStatus("loading")
    setError(null)
    load()
  }, [load])

  const connect = useCallback(async () => {
    active.current?.abort()
    const controller = new AbortController()
    active.current = controller
    setPending(true)
    setError(null)
    try {
      const authorizationUrl = await startGitHubConnection(
        request,
        controller.signal
      )
      window.location.assign(authorizationUrl)
    } catch (cause) {
      if (active.current === controller && mounted.current) {
        setError(
          cause instanceof Error ? cause.message : "Could not connect GitHub"
        )
      }
    } finally {
      if (active.current === controller) {
        active.current = null
        if (mounted.current) setPending(false)
      }
    }
  }, [request])

  const disconnect = useCallback(async () => {
    active.current?.abort()
    const controller = new AbortController()
    active.current = controller
    setPending(true)
    setError(null)
    try {
      await disconnectGitHub(request, controller.signal)
      if (active.current === controller && mounted.current) {
        setConnection(null)
      }
    } catch (cause) {
      if (active.current === controller && mounted.current) {
        setError(
          cause instanceof Error ? cause.message : "Could not disconnect GitHub"
        )
      }
    } finally {
      if (active.current === controller) {
        active.current = null
        if (mounted.current) setPending(false)
      }
    }
  }, [request])

  useEffect(() => {
    mounted.current = true
    load()
    return () => {
      mounted.current = false
      active.current?.abort()
    }
  }, [load])

  return {
    status,
    connection,
    installationUrl,
    pending,
    error,
    connect,
    disconnect,
    reload,
  }
}
