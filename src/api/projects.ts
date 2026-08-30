import type { ApiApp } from "./internal/environment";
import { requireAccount } from "./internal/accounts/authenticate";
import { withAccounts } from "./internal/accounts/bind";
import { readRequestBody } from "./internal/http/json";
import { withSessionCatalog } from "./internal/projects/bind";
import {
  createProject,
  getProject,
  listProjectItems,
  moveSessionToProject,
  updateProject,
} from "./internal/projects/handlers";
import { withWorkspaces } from "./internal/workspaces/bind";
import { requireWorkspaceMember } from "./internal/workspaces/member";

export function mapProjectEndpoints(app: ApiApp): void {
  const authorize = [
    withAccounts,
    requireAccount,
    withWorkspaces,
    requireWorkspaceMember,
    withSessionCatalog,
  ] as const;

  app.get(
    "/workspaces/:workspaceId/repositories/:repositoryId/items",
    ...authorize,
    listProjectItems,
  );
  app.post(
    "/workspaces/:workspaceId/repositories/:repositoryId/projects",
    ...authorize,
    readRequestBody,
    createProject,
  );
  app.get(
    "/workspaces/:workspaceId/repositories/:repositoryId/projects/:projectId",
    ...authorize,
    getProject,
  );
  app.patch(
    "/workspaces/:workspaceId/repositories/:repositoryId/projects/:projectId",
    ...authorize,
    readRequestBody,
    updateProject,
  );
  app.patch(
    "/workspaces/:workspaceId/repositories/:repositoryId/sessions/:sessionId",
    ...authorize,
    readRequestBody,
    moveSessionToProject,
  );
}
