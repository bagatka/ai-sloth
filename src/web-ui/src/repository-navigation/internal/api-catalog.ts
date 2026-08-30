import type { AuthenticatedRequest } from "@/authentication"
import { listGitHubRepositories } from "@/github"
import type {
  Page,
  ProjectDetails,
  RepositoryCatalog,
  RepositoryItem,
} from ".."

export function createRepositoryCatalog(
  request: AuthenticatedRequest
): RepositoryCatalog {
  return {
    async listRepositories(_workspaceId, cursor, signal) {
      const page = await listGitHubRepositories(request, cursor, signal)
      return {
        items: page.items.map(({ id, name, owner, defaultBranch }) => ({
          id,
          name,
          owner,
          defaultBranch,
        })),
        previousCursor: page.previousCursor,
        nextCursor: page.nextCursor,
      }
    },

    listRepositoryItems(workspaceId, repositoryId, cursor, signal) {
      return listItems(request, workspaceId, repositoryId, null, cursor, signal)
    },

    listProjectItems(workspaceId, repositoryId, projectId, cursor, signal) {
      return listItems(
        request,
        workspaceId,
        repositoryId,
        projectId,
        cursor,
        signal
      )
    },

    async createItem(
      workspaceId,
      repositoryId,
      parentProjectId,
      input,
      signal
    ) {
      if (input.kind === "project") {
        await requireOk(
          request(
            `/workspaces/${workspaceId}/repositories/${repositoryId}/projects`,
            jsonRequest("POST", { name: input.name, parentProjectId }, signal)
          ),
          "Could not create project"
        )
        return null
      }
      const response = await request(
        `/workspaces/${workspaceId}/sessions`,
        jsonRequest(
          "POST",
          {
            githubRepositoryId: repositoryId,
            branch: input.branch,
            prompt: input.prompt,
            name: input.name,
            projectId: parentProjectId,
          },
          signal,
          crypto.randomUUID()
        )
      )
      const body = await readJson(response)
      if (!response.ok) {
        throw new Error(readError(body) ?? "Could not start session")
      }
      if (!isSessionAccepted(body)) {
        throw new Error("The server returned an invalid session")
      }
      return {
        kind: "session",
        id: body.sessionId,
        name: input.name,
        status:
          body.status === "running"
            ? "running"
            : body.status === "finalizing"
              ? "waiting"
              : body.status === "succeeded"
                ? "completed"
                : "failed",
      }
    },

    async moveItem(workspaceId, repositoryId, item, targetProjectId, signal) {
      const path =
        item.kind === "project"
          ? `/workspaces/${workspaceId}/repositories/${repositoryId}/projects/${item.id}`
          : `/workspaces/${workspaceId}/repositories/${repositoryId}/sessions/${item.id}`
      const body =
        item.kind === "project"
          ? { parentProjectId: targetProjectId }
          : { projectId: targetProjectId }
      await requireOk(
        request(path, jsonRequest("PATCH", body, signal)),
        `Could not move ${item.kind}`
      )
    },

    async getProject(workspaceId, repositoryId, projectId, signal) {
      const response = await request(
        `/workspaces/${workspaceId}/repositories/${repositoryId}/projects/${projectId}`,
        { signal }
      )
      const body = await readJson(response)
      if (!response.ok)
        throw new Error(readError(body) ?? "Could not load project")
      if (!isProjectDetails(body)) {
        throw new Error("The server returned an invalid project")
      }
      return body
    },

    async updateProject(workspaceId, repositoryId, projectId, input, signal) {
      const response = await request(
        `/workspaces/${workspaceId}/repositories/${repositoryId}/projects/${projectId}`,
        jsonRequest("PATCH", input, signal)
      )
      const body = await readJson(response)
      if (!response.ok)
        throw new Error(readError(body) ?? "Could not update project")
      if (!isProjectDetails(body)) {
        throw new Error("The server returned an invalid project")
      }
      return body
    },
  }
}

async function listItems(
  request: AuthenticatedRequest,
  workspaceId: string,
  repositoryId: string,
  projectId: string | null,
  cursor: string | null,
  signal: AbortSignal
): Promise<Page<RepositoryItem>> {
  const query = new URLSearchParams()
  if (projectId) query.set("projectId", projectId)
  if (cursor) query.set("cursor", cursor)
  const suffix = query.size > 0 ? `?${query}` : ""
  const response = await request(
    `/workspaces/${workspaceId}/repositories/${repositoryId}/items${suffix}`,
    { signal }
  )
  const body = await readJson(response)
  if (!response.ok)
    throw new Error(readError(body) ?? "Could not load project items")
  if (!isItemPage(body)) {
    throw new Error("The server returned invalid project items")
  }
  return body
}

function jsonRequest(
  method: "POST" | "PATCH",
  body: unknown,
  signal: AbortSignal,
  idempotencyKey?: string
): RequestInit {
  return {
    method,
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
      ...(idempotencyKey ? { "Idempotency-Key": idempotencyKey } : {}),
    },
    body: JSON.stringify(body),
    signal,
  }
}

async function requireOk(
  pending: Promise<Response>,
  fallback: string
): Promise<void> {
  const response = await pending
  if (response.ok) return
  const body = await readJson(response)
  throw new Error(readError(body) ?? fallback)
}

async function readJson(response: Response): Promise<unknown> {
  if (!response.headers.get("Content-Type")?.includes("application/json")) {
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

function isItemPage(value: unknown): value is Page<RepositoryItem> {
  return (
    isObject(value) &&
    Array.isArray(value.items) &&
    value.items.every(isRepositoryItem) &&
    (value.previousCursor === null ||
      typeof value.previousCursor === "string") &&
    (value.nextCursor === null || typeof value.nextCursor === "string")
  )
}

function isRepositoryItem(value: unknown): value is RepositoryItem {
  return (
    isObject(value) &&
    typeof value.id === "string" &&
    typeof value.name === "string" &&
    (value.kind === "project" ||
      (value.kind === "session" &&
        (value.status === "running" ||
          value.status === "waiting" ||
          value.status === "completed" ||
          value.status === "failed")))
  )
}

function isSessionAccepted(value: unknown): value is {
  sessionId: string
  turnId: string
  status: "running" | "finalizing" | "succeeded" | "failed" | "interrupted"
} {
  return (
    isObject(value) &&
    typeof value.sessionId === "string" &&
    typeof value.turnId === "string" &&
    (value.status === "running" ||
      value.status === "finalizing" ||
      value.status === "succeeded" ||
      value.status === "failed" ||
      value.status === "interrupted")
  )
}

function isProjectDetails(value: unknown): value is ProjectDetails {
  return (
    isObject(value) &&
    value.kind === "project" &&
    typeof value.id === "string" &&
    typeof value.name === "string" &&
    (value.parentProjectId === null ||
      typeof value.parentProjectId === "string") &&
    typeof value.instructions === "string" &&
    Number.isSafeInteger(value.version) &&
    Number(value.version) > 0
  )
}

function isObject(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value)
}
