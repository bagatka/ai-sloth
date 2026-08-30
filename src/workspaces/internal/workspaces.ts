import type {
  WorkspaceOperations,
  WorkspaceOutcome,
} from "../workspaces";
import type { WorkspaceRepository } from "./store";
import {
  createInvitationToken,
  hashInvitationToken,
  isInvitationToken,
} from "./token";
import {
  isId,
  normalizeWorkspaceName,
} from "./validation";

export function createWorkspaceOperations(
  workspaces: WorkspaceRepository,
): WorkspaceOperations {
  return {
    async create(actor, input) {
      const name = normalizeWorkspaceName(input.name);
      if (!name) {
        return { ok: false, code: "invalid_input" };
      }

      try {
        const workspace = { id: crypto.randomUUID(), name };
        await workspaces.create(workspace, actor.userId);
        return { ok: true, value: workspace };
      } catch (error) {
        return internalFailure("Workspace creation failed", error);
      }
    },

    async list(actor) {
      try {
        return { ok: true, value: await workspaces.list(actor.userId) };
      } catch (error) {
        return internalFailure("Workspace listing failed", error);
      }
    },

    async requireMember(actor, workspaceId) {
      if (!isId(workspaceId)) {
        return { ok: false, code: "not_found" };
      }
      try {
        const workspace = await workspaces.findForMember(
          workspaceId,
          actor.userId,
        );
        return workspace
          ? { ok: true, value: workspace }
          : { ok: false, code: "not_member" };
      } catch (error) {
        return internalFailure("Workspace membership check failed", error);
      }
    },

    async listMembers(actor, workspaceId) {
      if (!isId(workspaceId)) {
        return { ok: false, code: "not_found" };
      }
      try {
        const members = await workspaces.listMembers(
          workspaceId,
          actor.userId,
        );
        return members
          ? { ok: true, value: members }
          : { ok: false, code: "not_member" };
      } catch (error) {
        return internalFailure("Workspace member listing failed", error);
      }
    },

    async createInvitation(actor, workspaceId) {
      if (!isId(workspaceId)) {
        return { ok: false, code: "not_found" };
      }
      try {
        const invitation = await createInvitationToken();
        const created = await workspaces.createInvitation(
          workspaceId,
          actor.userId,
          invitation,
        );
        return created
          ? {
              ok: true,
              value: {
                workspaceId,
                invitationToken: invitation.token,
                expiresAt: new Date(invitation.expiresAt).toISOString(),
              },
            }
          : { ok: false, code: "not_member" };
      } catch (error) {
        return internalFailure("Workspace invitation creation failed", error);
      }
    },

    async acceptInvitation(actor, invitationToken) {
      if (!isInvitationToken(invitationToken)) {
        return { ok: false, code: "invalid_invitation" };
      }

      try {
        const tokenHash = await hashInvitationToken(invitationToken);
        const now = Date.now();
        const invitation = await workspaces.findInvitation(tokenHash, now);
        if (!invitation) {
          return { ok: false, code: "invalid_invitation" };
        }
        const accepted = await workspaces.acceptInvitation(
          tokenHash,
          invitation.workspaceId,
          actor.userId,
          now,
        );
        if (!accepted) {
          return { ok: false, code: "invalid_invitation" };
        }
        const workspace = await workspaces.findForMember(
          invitation.workspaceId,
          actor.userId,
        );
        return workspace
          ? { ok: true, value: workspace }
          : { ok: false, code: "invalid_invitation" };
      } catch (error) {
        return internalFailure("Workspace invitation acceptance failed", error);
      }
    },

    async removeMember(actor, workspaceId, userId) {
      if (!isId(workspaceId) || !isId(userId)) {
        return { ok: false, code: "not_found" };
      }
      try {
        const result = await workspaces.removeMember(
          workspaceId,
          actor.userId,
          userId,
        );
        return result === "removed"
          ? { ok: true, value: undefined }
          : { ok: false, code: result };
      } catch (error) {
        return internalFailure("Workspace member removal failed", error);
      }
    },
  };
}

function internalFailure<T>(
  message: string,
  error: unknown,
): WorkspaceOutcome<T> {
  console.error(
    message,
    error instanceof Error ? error.message : "Unknown error",
  );
  return { ok: false, code: "internal_error" };
}
