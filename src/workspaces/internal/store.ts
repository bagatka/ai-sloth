import type {
  Workspace,
  WorkspaceMember,
} from "../workspaces";
import type { NewInvitation } from "./token";

const MAX_INVITATIONS_PER_WORKSPACE = 20;

export type StoredInvitation = {
  workspaceId: string;
};

export type RemoveMemberResult =
  | "removed"
  | "not_found"
  | "not_member"
  | "last_member";

export interface WorkspaceRepository {
  create(
    workspace: Workspace,
    creatorUserId: string,
  ): Promise<void>;
  list(userId: string): Promise<Workspace[]>;
  findForMember(
    workspaceId: string,
    userId: string,
  ): Promise<Workspace | null>;
  listMembers(
    workspaceId: string,
    actorUserId: string,
  ): Promise<WorkspaceMember[] | null>;
  createInvitation(
    workspaceId: string,
    userId: string,
    invitation: NewInvitation,
  ): Promise<boolean>;
  findInvitation(tokenHash: string, now: number): Promise<StoredInvitation | null>;
  acceptInvitation(
    tokenHash: string,
    workspaceId: string,
    userId: string,
    now: number,
  ): Promise<boolean>;
  removeMember(
    workspaceId: string,
    actorUserId: string,
    userId: string,
  ): Promise<RemoveMemberResult>;
}

export class D1WorkspaceRepository implements WorkspaceRepository {
  constructor(private readonly database: D1Database) {}

  async create(
    workspace: Workspace,
    creatorUserId: string,
  ): Promise<void> {
    const writes = await this.database.batch([
      this.database.prepare(INSERT_WORKSPACE_SQL).bind(
        workspace.id,
        workspace.name,
      ),
      this.database.prepare(INSERT_MEMBERSHIP_SQL).bind(
        workspace.id,
        creatorUserId,
      ),
    ]);
    requireSuccessfulWrites(writes, "Could not create workspace");
  }

  async list(userId: string): Promise<Workspace[]> {
    const stored = await this.database
      .prepare(LIST_WORKSPACES_SQL)
      .bind(userId)
      .all<Workspace>();
    if (!stored.success) {
      throw new Error("Could not list workspaces");
    }
    return stored.results;
  }

  findForMember(
    workspaceId: string,
    userId: string,
  ): Promise<Workspace | null> {
    return this.database
      .prepare(FIND_WORKSPACE_FOR_MEMBER_SQL)
      .bind(workspaceId, userId)
      .first<Workspace>();
  }

  async listMembers(
    workspaceId: string,
    actorUserId: string,
  ): Promise<WorkspaceMember[] | null> {
    const stored = await this.database
      .prepare(LIST_MEMBERS_SQL)
      .bind(workspaceId, workspaceId, actorUserId)
      .all<WorkspaceMember>();
    if (!stored.success) {
      throw new Error("Could not list workspace members");
    }
    return stored.results.length > 0 ? stored.results : null;
  }

  async createInvitation(
    workspaceId: string,
    userId: string,
    invitation: NewInvitation,
  ): Promise<boolean> {
    const writes = await this.database.batch([
      this.database.prepare(DELETE_EXPIRED_INVITATIONS_SQL).bind(
        workspaceId,
        invitation.createdAt,
        workspaceId,
        userId,
      ),
      this.database.prepare(TRIM_INVITATIONS_SQL).bind(
        workspaceId,
        workspaceId,
        MAX_INVITATIONS_PER_WORKSPACE - 1,
        workspaceId,
        userId,
      ),
      this.database.prepare(INSERT_INVITATION_SQL).bind(
        invitation.tokenHash,
        workspaceId,
        invitation.expiresAt,
        invitation.createdAt,
        workspaceId,
        userId,
      ),
    ]);
    requireSuccessfulWrites(writes, "Could not create workspace invitation");
    return changed(writes[2]);
  }

  findInvitation(
    tokenHash: string,
    now: number,
  ): Promise<StoredInvitation | null> {
    return this.database
      .prepare(FIND_INVITATION_SQL)
      .bind(tokenHash, now)
      .first<StoredInvitation>();
  }

  async acceptInvitation(
    tokenHash: string,
    workspaceId: string,
    userId: string,
    now: number,
  ): Promise<boolean> {
    const writes = await this.database.batch([
      this.database.prepare(ACCEPT_INVITATION_SQL).bind(
        userId,
        tokenHash,
        now,
      ),
      this.database.prepare(DELETE_INVITATION_SQL).bind(tokenHash),
    ]);
    requireSuccessfulWrites(writes, "Could not accept workspace invitation");
    if (changed(writes[0])) {
      return true;
    }

    return await this.findForMember(workspaceId, userId) !== null;
  }

  async removeMember(
    workspaceId: string,
    actorUserId: string,
    userId: string,
  ): Promise<RemoveMemberResult> {
    const removed = await this.database
      .prepare(REMOVE_MEMBER_SQL)
      .bind(
        workspaceId,
        userId,
        workspaceId,
        actorUserId,
        workspaceId,
      )
      .run();
    if (!removed.success) {
      throw new Error("Could not remove workspace member");
    }
    if (changed(removed)) {
      return "removed";
    }

    const workspace = await this.database
      .prepare(FIND_WORKSPACE_SQL)
      .bind(workspaceId)
      .first();
    if (!workspace) {
      return "not_found";
    }
    const actor = await this.findForMember(workspaceId, actorUserId);
    if (!actor) {
      return "not_member";
    }
    const target = await this.findForMember(workspaceId, userId);
    if (!target) {
      return "not_found";
    }
    return "last_member";
  }
}

function requireSuccessfulWrites(
  writes: D1Result[],
  message: string,
): void {
  if (writes.some((write) => !write.success)) {
    throw new Error(message);
  }
}

function changed(write: D1Result): boolean {
  return (write.meta.changes ?? 0) > 0;
}

const INSERT_WORKSPACE_SQL = `
  INSERT INTO workspaces (id, name)
  VALUES (?, ?)
`;

const INSERT_MEMBERSHIP_SQL = `
  INSERT INTO workspace_memberships (workspace_id, user_id)
  VALUES (?, ?)
`;

const LIST_WORKSPACES_SQL = `
  SELECT workspaces.id, workspaces.name
  FROM workspace_memberships
  JOIN workspaces
    ON workspaces.id = workspace_memberships.workspace_id
  WHERE workspace_memberships.user_id = ?
  ORDER BY workspaces.created_at, workspaces.id
`;

const FIND_WORKSPACE_FOR_MEMBER_SQL = `
  SELECT workspaces.id, workspaces.name
  FROM workspaces
  JOIN workspace_memberships
    ON workspace_memberships.workspace_id = workspaces.id
  WHERE workspaces.id = ? AND workspace_memberships.user_id = ?
`;

const FIND_WORKSPACE_SQL = `
  SELECT id
  FROM workspaces
  WHERE id = ?
`;

const LIST_MEMBERS_SQL = `
  SELECT user_id AS userId
  FROM workspace_memberships
  WHERE workspace_id = ?
    AND EXISTS (
      SELECT 1
      FROM workspace_memberships
      WHERE workspace_id = ? AND user_id = ?
    )
  ORDER BY created_at, user_id
`;

const DELETE_EXPIRED_INVITATIONS_SQL = `
  DELETE FROM workspace_invitations
  WHERE workspace_id = ?
    AND expires_at <= ?
    AND EXISTS (
      SELECT 1
      FROM workspace_memberships
      WHERE workspace_id = ? AND user_id = ?
    )
`;

const TRIM_INVITATIONS_SQL = `
  DELETE FROM workspace_invitations
  WHERE workspace_id = ? AND token_hash NOT IN (
    SELECT token_hash
    FROM workspace_invitations
    WHERE workspace_id = ?
    ORDER BY created_at DESC
    LIMIT ?
  )
  AND EXISTS (
    SELECT 1
    FROM workspace_memberships
    WHERE workspace_id = ? AND user_id = ?
  )
`;

const INSERT_INVITATION_SQL = `
  INSERT INTO workspace_invitations (
    token_hash,
    workspace_id,
    expires_at,
    created_at
  )
  SELECT ?, ?, ?, ?
  WHERE EXISTS (
    SELECT 1
    FROM workspace_memberships
    WHERE workspace_id = ? AND user_id = ?
  )
`;

const FIND_INVITATION_SQL = `
  SELECT workspace_id AS workspaceId
  FROM workspace_invitations
  WHERE token_hash = ? AND expires_at > ?
`;

const ACCEPT_INVITATION_SQL = `
  INSERT OR IGNORE INTO workspace_memberships (workspace_id, user_id)
  SELECT workspace_id, ?
  FROM workspace_invitations
  WHERE token_hash = ? AND expires_at > ?
`;

const DELETE_INVITATION_SQL = `
  DELETE FROM workspace_invitations
  WHERE token_hash = ?
`;

const REMOVE_MEMBER_SQL = `
  DELETE FROM workspace_memberships
  WHERE workspace_id = ?
    AND user_id = ?
    AND EXISTS (
      SELECT 1
      FROM workspace_memberships
      WHERE workspace_id = ? AND user_id = ?
    )
    AND (
      SELECT COUNT(*)
      FROM workspace_memberships
      WHERE workspace_id = ?
    ) > 1
`;
