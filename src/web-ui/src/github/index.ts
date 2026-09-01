import type { AuthenticatedRequest } from "@/authentication"

export type GitHubConnection = {
  githubUserId: string
  login: string
}

export type GitHub = {
  status: "loading" | "ready" | "error"
  connection: GitHubConnection | null
  installationUrl: string | null
  pending: boolean
  error: string | null
  connect(): Promise<void>
  disconnect(): Promise<void>
  reload(): void
}

export { useGitHub } from "./internal/use-github"
export { listGitHubRepositories } from "./internal/api"
export type { GitHubRepositoryPage } from "./internal/api"

export type GitHubRequest = AuthenticatedRequest
