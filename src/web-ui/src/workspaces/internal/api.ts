import type { AuthenticatedRequest } from "@/authentication"
import type { Workspace } from ".."

type ApiOutcome<T> = { ok: true; value: T } | { ok: false; error: string }

export async function listWorkspaces(
  request: AuthenticatedRequest,
  signal: AbortSignal
): Promise<ApiOutcome<Workspace[]>> {
  const response = await request("/workspaces", { signal })
  const body = await readJson(response)
  if (!response.ok) {
    return workspaceFailure(body)
  }
  if (
    !isObject(body) ||
    !Array.isArray(body.workspaces) ||
    !body.workspaces.every(isWorkspace)
  ) {
    return invalidResponse()
  }
  return { ok: true, value: body.workspaces }
}

export async function createWorkspace(
  request: AuthenticatedRequest,
  name: string,
  signal: AbortSignal
): Promise<ApiOutcome<Workspace>> {
  return workspaceRequest(request, "/workspaces", { name }, signal)
}

export async function joinWorkspace(
  request: AuthenticatedRequest,
  invitationCode: string,
  signal: AbortSignal
): Promise<ApiOutcome<Workspace>> {
  return workspaceRequest(
    request,
    "/workspace-invitations/accept",
    { invitationToken: invitationCode },
    signal
  )
}

async function workspaceRequest(
  request: AuthenticatedRequest,
  path: string,
  body: Record<string, string>,
  signal: AbortSignal
): Promise<ApiOutcome<Workspace>> {
  const response = await request(path, {
    method: "POST",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
    },
    body: JSON.stringify(body),
    signal,
  })
  const responseBody = await readJson(response)
  if (!response.ok) {
    return workspaceFailure(responseBody)
  }
  return isWorkspace(responseBody)
    ? { ok: true, value: responseBody }
    : invalidResponse()
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

function workspaceFailure<T>(body: unknown): ApiOutcome<T> {
  return {
    ok: false,
    error: readError(body) ?? "Workspace operation failed",
  }
}

function invalidResponse<T>(): ApiOutcome<T> {
  return { ok: false, error: "The server returned an invalid response" }
}

function readError(value: unknown): string | null {
  return isObject(value) && typeof value.error === "string" ? value.error : null
}

function isWorkspace(value: unknown): value is Workspace {
  return (
    isObject(value) &&
    typeof value.id === "string" &&
    typeof value.name === "string"
  )
}

function isObject(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value)
}
