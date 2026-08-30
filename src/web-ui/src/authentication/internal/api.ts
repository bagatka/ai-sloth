import type { Account, Credentials } from ".."

type ApiOutcome<T> = { ok: true; value: T } | { ok: false; error: string }

export type AccountSession = {
  account: Account
  token: string
  expiresAt: number
}

export async function createAccountSession(
  operation: "login" | "register",
  credentials: Credentials,
  signal: AbortSignal
): Promise<ApiOutcome<AccountSession>> {
  const response = await fetch(`/auth/${operation}`, {
    method: "POST",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
    },
    body: JSON.stringify(credentials),
    cache: "no-store",
    credentials: "omit",
    signal,
  })
  const body = await readJson(response)

  if (!response.ok) {
    return { ok: false, error: readError(body) ?? "Authentication failed" }
  }
  if (!isObject(body)) {
    return { ok: false, error: "The server returned an invalid response" }
  }

  const token = body.sessionToken
  const account = body.user
  const expiresAt =
    typeof body.expiresAt === "string" ? Date.parse(body.expiresAt) : Number.NaN
  if (
    typeof token !== "string" ||
    token.length === 0 ||
    !isAccount(account) ||
    !Number.isFinite(expiresAt) ||
    expiresAt <= Date.now()
  ) {
    return { ok: false, error: "The server returned an invalid response" }
  }

  return { ok: true, value: { account, token, expiresAt } }
}

export async function authenticatedRequest(
  token: string,
  path: string,
  init?: RequestInit
): Promise<Response> {
  const url = resolveSameOriginPath(path)

  const headers = new Headers(init?.headers)
  headers.set("Authorization", `Bearer ${token}`)
  return fetch(url, {
    ...init,
    headers,
    cache: "no-store",
    credentials: "omit",
  })
}

function resolveSameOriginPath(path: string): URL {
  try {
    const url = new URL(path, window.location.origin)
    if (path.startsWith("/") && url.origin === window.location.origin) {
      return url
    }
  } catch {
    // Report every invalid URL through the same stable contract below.
  }
  throw new Error("Authenticated requests require a same-origin path")
}

export async function revokeAccountSession(
  token: string,
  signal: AbortSignal
): Promise<ApiOutcome<undefined>> {
  const response = await authenticatedRequest(token, "/auth/logout", {
    method: "POST",
    signal,
  })
  if (response.status === 204 || response.status === 401) {
    return { ok: true, value: undefined }
  }

  const body = await readJson(response)
  return {
    ok: false,
    error: readError(body) ?? "Sign out failed; please try again",
  }
}

async function readJson(response: Response): Promise<unknown> {
  const contentType = response.headers.get("Content-Type")
  if (!contentType?.includes("application/json")) {
    return null
  }
  try {
    return await response.json()
  } catch {
    return null
  }
}

function readError(value: unknown): string | null {
  return isObject(value) && typeof value.error === "string" ? value.error : null
}

function isAccount(value: unknown): value is Account {
  return (
    isObject(value) &&
    typeof value.id === "string" &&
    typeof value.email === "string"
  )
}

function isObject(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value)
}
