import type { GitHubConnection, GitHubRequest } from ".."

export type GitHubRepositoryPage = {
  items: Array<{
    id: string
    name: string
    owner: string
    fullName: string
    defaultBranch: string
    private: boolean
    canPush: boolean
  }>
  previousCursor: string | null
  nextCursor: string | null
}

type ConnectionStatus = {
  connection: GitHubConnection | null
  installationUrl: string | null
}

export async function getGitHubConnection(
  request: GitHubRequest,
  signal: AbortSignal
): Promise<ConnectionStatus> {
  const response = await request("/github/connection", { signal })
  const body = await readJson(response)
  if (!response.ok) throw new Error(readError(body) ?? "Could not load GitHub")
  if (!isObject(body))
    throw new Error("The server returned an invalid response")
  const connection = body.connection
  const installationUrl = body.installationUrl
  if (
    connection !== null &&
    (!isObject(connection) ||
      typeof connection.githubUserId !== "string" ||
      typeof connection.login !== "string")
  ) {
    throw new Error("The server returned an invalid response")
  }
  if (installationUrl !== null && typeof installationUrl !== "string") {
    throw new Error("The server returned an invalid response")
  }
  return {
    connection: connection as GitHubConnection | null,
    installationUrl,
  }
}

export async function startGitHubConnection(
  request: GitHubRequest,
  signal: AbortSignal
): Promise<string> {
  const response = await request("/github/connection", {
    method: "POST",
    signal,
  })
  const body = await readJson(response)
  if (
    !response.ok ||
    !isObject(body) ||
    typeof body.authorizationUrl !== "string"
  ) {
    throw new Error(readError(body) ?? "Could not connect GitHub")
  }
  const url = new URL(body.authorizationUrl)
  if (url.protocol !== "https:" || url.hostname !== "github.com") {
    throw new Error("The server returned an invalid GitHub URL")
  }
  return url.toString()
}

export async function disconnectGitHub(
  request: GitHubRequest,
  signal: AbortSignal
): Promise<void> {
  const response = await request("/github/connection", {
    method: "DELETE",
    signal,
  })
  if (response.status === 204) return
  const body = await readJson(response)
  throw new Error(readError(body) ?? "Could not disconnect GitHub")
}

export async function listGitHubRepositories(
  request: GitHubRequest,
  cursor: string | null,
  signal: AbortSignal
): Promise<GitHubRepositoryPage> {
  const query = cursor ? `?cursor=${encodeURIComponent(cursor)}` : ""
  const response = await request(`/github/repositories${query}`, { signal })
  const body = await readJson(response)
  if (!response.ok) {
    throw new Error(readError(body) ?? "Could not load GitHub repositories")
  }
  if (!isRepositoryPage(body)) {
    throw new Error("The server returned invalid GitHub repositories")
  }
  return body
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

function isRepositoryPage(value: unknown): value is GitHubRepositoryPage {
  return (
    isObject(value) &&
    Array.isArray(value.items) &&
    value.items.every(
      (item) =>
        isObject(item) &&
        typeof item.id === "string" &&
        typeof item.name === "string" &&
        typeof item.owner === "string" &&
        typeof item.fullName === "string" &&
        typeof item.defaultBranch === "string" &&
        typeof item.private === "boolean" &&
        typeof item.canPush === "boolean"
    ) &&
    (value.previousCursor === null ||
      typeof value.previousCursor === "string") &&
    (value.nextCursor === null || typeof value.nextCursor === "string")
  )
}

function isObject(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value)
}
