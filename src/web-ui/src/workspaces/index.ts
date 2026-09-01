export type Workspace = {
  id: string
  name: string
}

export type WorkspaceOutcome =
  { ok: true; value: Workspace } | { ok: false; error: string }

export type Workspaces = {
  status: "loading" | "ready" | "error"
  workspaces: readonly Workspace[]
  error: string | null
  pending: "create" | "join" | null
  create(name: string): Promise<WorkspaceOutcome>
  join(invitationCode: string): Promise<WorkspaceOutcome>
  reload(): void
}

export { useWorkspaces } from "./internal/use-workspaces"
