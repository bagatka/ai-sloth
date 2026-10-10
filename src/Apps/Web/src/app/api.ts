import createClient from "openapi-fetch"

import type { components, paths } from "@/api/schema"
import { apiUrl } from "./host"

// The host's public API, typed from its openapi.json (npm run api). Every call carries the browser's
// session cookie, which the host set when this browser signed in; no script here ever sees the token.
export const api = createClient<paths>({
  baseUrl: apiUrl.replace(/\/$/, ""),
  credentials: "include",
})

export type Schemas = components["schemas"]

/** A refusal from the host, as its problem details describe it. */
export class ApiError extends Error {
  readonly status: number
  /** The host's error code, such as "users.code_not_found", when it gives one. */
  readonly code: string | undefined
  /** Rejected fields and why, for a refused request. */
  readonly fields: Record<string, string[]>

  constructor(
    status: number,
    message: string,
    code: string | undefined,
    fields: Record<string, string[]>
  ) {
    super(message)
    this.name = "ApiError"
    this.status = status
    this.code = code
    this.fields = fields
  }
}

type Problem = {
  title?: string | null
  detail?: string | null
  code?: string
  errors?: Record<string, string[]>
}

/**
 * The value of a call, or its refusal thrown as an ApiError; a host that can't be reached throws what
 * fetch threw.
 */
export async function answer<T>(
  call: Promise<{ data?: T; error?: unknown; response: Response }>
): Promise<T> {
  const { data, error, response } = await call
  if (response.ok) {
    return data as T
  }

  const problem = (typeof error === "object" && error ? error : {}) as Problem
  const message =
    problem.detail ||
    problem.title ||
    (typeof error === "string" && error) ||
    `The host answered ${response.status}.`
  throw new ApiError(
    response.status,
    message,
    problem.code,
    problem.errors ?? {}
  )
}

/** Whether a failure means this browser isn't signed in, or no longer is. */
export function isSignedOut(error: unknown): boolean {
  return error instanceof ApiError && error.status === 401
}
