import { expect, test } from "bun:test";
import type { Actor } from "@ai-sloth/accounts";
import type {
  Workspace,
  WorkspaceMember,
} from "../workspaces";
import { createWorkspaceOperations } from "./workspaces";
import type {
  WorkspaceRepository,
  RemoveMemberResult,
  StoredInvitation,
} from "./store";
import type { NewInvitation } from "./token";

class MemoryWorkspaces implements WorkspaceRepository {
  private readonly workspaces = new Map<string, Workspace>();
  private readonly members = new Map<string, Set<string>>();
  private readonly invitations = new Map<
    string,
    { workspaceId: string; expiresAt: number }
  >();

  async create(workspace: Workspace, userId: string): Promise<void> {
    this.workspaces.set(workspace.id, workspace);
    this.members.set(workspace.id, new Set([userId]));
  }

  async list(userId: string): Promise<Workspace[]> {
    return [...this.workspaces.values()].filter((workspace) =>
      this.members.get(workspace.id)?.has(userId)
    );
  }

  async findForMember(
    workspaceId: string,
    userId: string,
  ): Promise<Workspace | null> {
    return this.members.get(workspaceId)?.has(userId)
      ? this.workspaces.get(workspaceId) ?? null
      : null;
  }

  async listMembers(
    workspaceId: string,
    actorUserId: string,
  ): Promise<WorkspaceMember[] | null> {
    const members = this.members.get(workspaceId);
    return members?.has(actorUserId)
      ? [...members].map((userId) => ({ userId }))
      : null;
  }

  async createInvitation(
    workspaceId: string,
    userId: string,
    invitation: NewInvitation,
  ): Promise<boolean> {
    if (!this.members.get(workspaceId)?.has(userId)) {
      return false;
    }
    this.invitations.set(invitation.tokenHash, {
      workspaceId,
      expiresAt: invitation.expiresAt,
    });
    return true;
  }

  async findInvitation(
    tokenHash: string,
    now: number,
  ): Promise<StoredInvitation | null> {
    const invitation = this.invitations.get(tokenHash);
    return invitation && invitation.expiresAt > now
      ? { workspaceId: invitation.workspaceId }
      : null;
  }

  async acceptInvitation(
    tokenHash: string,
    workspaceId: string,
    userId: string,
    now: number,
  ): Promise<boolean> {
    const invitation = this.invitations.get(tokenHash);
    if (
      !invitation
      || invitation.workspaceId !== workspaceId
      || invitation.expiresAt <= now
    ) {
      return false;
    }
    this.invitations.delete(tokenHash);
    this.members.get(workspaceId)?.add(userId);
    return true;
  }

  async removeMember(
    workspaceId: string,
    actorUserId: string,
    userId: string,
  ): Promise<RemoveMemberResult> {
    const members = this.members.get(workspaceId);
    if (!members) return "not_found";
    if (!members.has(actorUserId)) return "not_member";
    if (!members.has(userId)) return "not_found";
    if (members.size === 1) return "last_member";
    members.delete(userId);
    return "removed";
  }
}

const actor = (userId: string): Actor => ({ userId });

test("members can invite and remove other members as equals", async () => {
  const workspaces = createWorkspaceOperations(
    new MemoryWorkspaces(),
  );
  const first = actor("b47f6e35-b7f3-4c6f-91f6-93f0479ec15b");
  const second = actor("a47f6e35-b7f3-4c6f-91f6-93f0479ec15b");

  const created = await workspaces.create(first, { name: "Example" });
  expect(created.ok).toBeTrue();
  if (!created.ok) return;

  expect(await workspaces.listMembers(second, created.value.id)).toEqual({
    ok: false,
    code: "not_member",
  });

  const invitation = await workspaces.createInvitation(
    first,
    created.value.id,
  );
  expect(invitation.ok).toBeTrue();
  if (!invitation.ok) return;
  expect(invitation.value.invitationToken).toStartWith("asl_workspace_invite_");

  expect(await workspaces.acceptInvitation(
    second,
    invitation.value.invitationToken,
  )).toEqual({ ok: true, value: created.value });
  expect(await workspaces.listMembers(second, created.value.id)).toEqual({
    ok: true,
    value: [{ userId: first.userId }, { userId: second.userId }],
  });

  expect(await workspaces.removeMember(
    second,
    created.value.id,
    first.userId,
  )).toEqual({ ok: true, value: undefined });
  expect(await workspaces.removeMember(
    second,
    created.value.id,
    second.userId,
  )).toEqual({ ok: false, code: "last_member" });
});
