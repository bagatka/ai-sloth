import type { AccountSession } from "./api"

const SESSION_STORAGE_KEY = "ai-sloth-account-session"

export function readAccountSession(): AccountSession | null {
  try {
    const value: unknown = JSON.parse(
      sessionStorage.getItem(SESSION_STORAGE_KEY) ?? "null"
    )
    if (!isAccountSession(value)) {
      sessionStorage.removeItem(SESSION_STORAGE_KEY)
      return null
    }
    return value
  } catch {
    return null
  }
}

export function storeAccountSession(session: AccountSession): void {
  try {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session))
  } catch {
    // Authentication still works in memory when browser storage is unavailable.
  }
}

export function removeAccountSession(): void {
  try {
    sessionStorage.removeItem(SESSION_STORAGE_KEY)
  } catch {
    // The in-memory session is still cleared below this boundary.
  }
}

function isAccountSession(value: unknown): value is AccountSession {
  return (
    isObject(value) &&
    isObject(value.account) &&
    typeof value.account.id === "string" &&
    typeof value.account.email === "string" &&
    typeof value.token === "string" &&
    value.token.length > 0 &&
    typeof value.expiresAt === "number" &&
    Number.isFinite(value.expiresAt) &&
    value.expiresAt > Date.now()
  )
}

function isObject(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value)
}
