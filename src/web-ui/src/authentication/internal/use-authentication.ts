import { useCallback, useEffect, useRef, useState } from "react"
import type {
  Account,
  Authentication,
  AuthenticationOutcome,
  Credentials,
} from ".."
import {
  authenticatedRequest as sendAuthenticatedRequest,
  createAccountSession,
  revokeAccountSession,
  type AccountSession,
} from "./api"
import {
  readAccountSession,
  removeAccountSession,
  storeAccountSession,
} from "./session-storage"

const MAX_TIMEOUT_MILLISECONDS = 2_147_483_647

export function useAuthentication(): Authentication {
  const [initialSession] = useState(readAccountSession)
  const token = useRef<string | null>(initialSession?.token ?? null)
  const expirationTimer = useRef<number | null>(null)
  const activeRequest = useRef<AbortController | null>(null)
  const mounted = useRef(false)
  const [account, setAccount] = useState<Account | null>(
    initialSession?.account ?? null
  )
  const [pending, setPending] = useState(false)

  const clearExpirationTimer = useCallback(() => {
    if (expirationTimer.current === null) return
    window.clearTimeout(expirationTimer.current)
    expirationTimer.current = null
  }, [])

  const clearSession = useCallback(() => {
    token.current = null
    clearExpirationTimer()
    removeAccountSession()
    if (mounted.current) setAccount(null)
  }, [clearExpirationTimer])

  const request = useCallback(
    async (path: string, init?: RequestInit) => {
      const currentToken = token.current
      if (!currentToken) {
        throw new Error("Authentication is required")
      }

      const response = await sendAuthenticatedRequest(currentToken, path, init)
      if (response.status === 401) {
        clearSession()
      }
      return response
    },
    [clearSession]
  )

  const scheduleExpiration = useCallback(
    (expiresAt: number) => {
      clearExpirationTimer()
      expirationTimer.current = window.setTimeout(
        clearSession,
        Math.min(expiresAt - Date.now(), MAX_TIMEOUT_MILLISECONDS)
      )
    },
    [clearExpirationTimer, clearSession]
  )

  const storeSession = useCallback(
    (session: AccountSession) => {
      token.current = session.token
      storeAccountSession(session)
      scheduleExpiration(session.expiresAt)
      if (mounted.current) setAccount(session.account)
    },
    [scheduleExpiration]
  )

  const beginRequest = useCallback(() => {
    activeRequest.current?.abort()
    const controller = new AbortController()
    activeRequest.current = controller
    if (mounted.current) {
      setPending(true)
    }
    return controller
  }, [])

  const finishRequest = useCallback((controller: AbortController) => {
    if (activeRequest.current === controller) {
      activeRequest.current = null
      if (mounted.current) {
        setPending(false)
      }
    }
  }, [])

  const authenticate = useCallback(
    async (
      operation: "login" | "register",
      credentials: Credentials
    ): Promise<AuthenticationOutcome> => {
      const controller = beginRequest()
      try {
        const outcome = await createAccountSession(
          operation,
          credentials,
          controller.signal
        )
        if (outcome.ok && activeRequest.current === controller) {
          storeSession(outcome.value)
        }
        return outcome.ok ? { ok: true } : outcome
      } catch {
        return {
          ok: false,
          error: controller.signal.aborted
            ? "Authentication was canceled"
            : "Could not reach the authentication service",
        }
      } finally {
        finishRequest(controller)
      }
    },
    [beginRequest, finishRequest, storeSession]
  )

  const register = useCallback(
    (credentials: Credentials) => authenticate("register", credentials),
    [authenticate]
  )

  const signIn = useCallback(
    (credentials: Credentials) => authenticate("login", credentials),
    [authenticate]
  )

  const signOut = useCallback(async (): Promise<AuthenticationOutcome> => {
    const currentToken = token.current
    if (!currentToken) {
      clearSession()
      return { ok: true }
    }

    const controller = beginRequest()
    try {
      const outcome = await revokeAccountSession(
        currentToken,
        controller.signal
      )
      if (outcome.ok && activeRequest.current === controller) {
        clearSession()
      }
      return outcome.ok ? { ok: true } : outcome
    } catch {
      return {
        ok: false,
        error: controller.signal.aborted
          ? "Sign out was canceled"
          : "Could not reach the authentication service",
      }
    } finally {
      finishRequest(controller)
    }
  }, [beginRequest, clearSession, finishRequest])

  useEffect(() => {
    mounted.current = true
    if (initialSession) scheduleExpiration(initialSession.expiresAt)
    return () => {
      mounted.current = false
      activeRequest.current?.abort()
      clearExpirationTimer()
    }
  }, [clearExpirationTimer, initialSession, scheduleExpiration])

  return {
    authenticated: account !== null,
    account,
    pending,
    request,
    register,
    signIn,
    signOut,
  }
}
