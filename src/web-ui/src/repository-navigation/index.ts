export type Repository = {
  id: string
  name: string
  owner: string
  defaultBranch: string
}

export type Project = {
  kind: "project"
  id: string
  name: string
}

export type ProjectDetails = Project & {
  parentProjectId: string | null
  instructions: string
  version: number
}

export type Session = {
  kind: "session"
  id: string
  name: string
  status: "running" | "waiting" | "completed" | "failed"
}

export type RepositoryItem = Project | Session

export type NewRepositoryItem =
  | { kind: "project"; name: string }
  | { kind: "session"; name: string; branch: string; prompt: string }

export type Page<T> = {
  items: readonly T[]
  previousCursor: string | null
  nextCursor: string | null
}

export type RepositoryCatalog = {
  listRepositories(
    workspaceId: string,
    cursor: string | null,
    signal: AbortSignal
  ): Promise<Page<Repository>>
  listRepositoryItems(
    workspaceId: string,
    repositoryId: string,
    cursor: string | null,
    signal: AbortSignal
  ): Promise<Page<RepositoryItem>>
  listProjectItems(
    workspaceId: string,
    repositoryId: string,
    projectId: string,
    cursor: string | null,
    signal: AbortSignal
  ): Promise<Page<RepositoryItem>>
  createItem(
    workspaceId: string,
    repositoryId: string,
    parentProjectId: string | null,
    input: NewRepositoryItem,
    signal: AbortSignal
  ): Promise<Session | null>
  moveItem(
    workspaceId: string,
    repositoryId: string,
    item: Pick<RepositoryItem, "kind" | "id">,
    targetProjectId: string | null,
    signal: AbortSignal
  ): Promise<void>
  getProject(
    workspaceId: string,
    repositoryId: string,
    projectId: string,
    signal: AbortSignal
  ): Promise<ProjectDetails>
  updateProject(
    workspaceId: string,
    repositoryId: string,
    projectId: string,
    input: { name: string; instructions: string; expectedVersion: number },
    signal: AbortSignal
  ): Promise<ProjectDetails>
}

export { createInMemoryRepositoryCatalog } from "./internal/in-memory-catalog"
export { createRepositoryCatalog } from "./internal/api-catalog"
export { RepositoryNavigation } from "./internal/repository-navigation"
