import type { ApiApp } from "./internal/environment";
import { requireAccount } from "./internal/accounts/authenticate";
import { withAccounts } from "./internal/accounts/bind";
import { readRequestBody } from "./internal/http/json";
import { withWorkspaces } from "./internal/workspaces/bind";
import {
  acceptInvitation,
  createInvitation,
  createWorkspace,
  listMembers,
  listWorkspaces,
  removeMember,
} from "./internal/workspaces/handlers";

export function mapWorkspaceEndpoints(app: ApiApp): void {
  app.get(
    "/workspaces",
    withAccounts,
    requireAccount,
    withWorkspaces,
    listWorkspaces,
  );
  app.post(
    "/workspaces",
    withAccounts,
    requireAccount,
    withWorkspaces,
    readRequestBody,
    createWorkspace,
  );
  app.get(
    "/workspaces/:workspaceId/members",
    withAccounts,
    requireAccount,
    withWorkspaces,
    listMembers,
  );
  app.delete(
    "/workspaces/:workspaceId/members/:userId",
    withAccounts,
    requireAccount,
    withWorkspaces,
    removeMember,
  );
  app.post(
    "/workspaces/:workspaceId/invitations",
    withAccounts,
    requireAccount,
    withWorkspaces,
    createInvitation,
  );
  app.post(
    "/workspace-invitations/accept",
    withAccounts,
    requireAccount,
    withWorkspaces,
    readRequestBody,
    acceptInvitation,
  );
}
