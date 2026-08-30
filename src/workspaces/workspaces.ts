import type { Actor } from "@ai-sloth/accounts";

export type Workspace = {
  id: string;
  name: string;
};

export type WorkspaceMember = {
  userId: string;
};

export type WorkspaceInvitation = {
  workspaceId: string;
  invitationToken: string;
  expiresAt: string;
};

export type CreateWorkspaceInput = {
  name: string;
};

export type WorkspaceFailureCode =
  | "invalid_input"
  | "not_found"
  | "not_member"
  | "last_member"
  | "invalid_invitation"
  | "internal_error";

export type WorkspaceOutcome<T> =
  | { ok: true; value: T }
  | { ok: false; code: WorkspaceFailureCode };

export type WorkspaceOperations = {
  create(
    actor: Actor,
    input: CreateWorkspaceInput,
  ): Promise<WorkspaceOutcome<Workspace>>;
  list(actor: Actor): Promise<WorkspaceOutcome<Workspace[]>>;
  requireMember(
    actor: Actor,
    workspaceId: string,
  ): Promise<WorkspaceOutcome<Workspace>>;
  listMembers(
    actor: Actor,
    workspaceId: string,
  ): Promise<WorkspaceOutcome<WorkspaceMember[]>>;
  createInvitation(
    actor: Actor,
    workspaceId: string,
  ): Promise<WorkspaceOutcome<WorkspaceInvitation>>;
  acceptInvitation(
    actor: Actor,
    invitationToken: string,
  ): Promise<WorkspaceOutcome<Workspace>>;
  removeMember(
    actor: Actor,
    workspaceId: string,
    userId: string,
  ): Promise<WorkspaceOutcome<undefined>>;
};
