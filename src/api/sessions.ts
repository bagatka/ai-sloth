import type { ApiApp } from "./internal/environment";
import { requireAccount } from "./internal/accounts/authenticate";
import { withAccounts } from "./internal/accounts/bind";
import { readRequestBody } from "./internal/http/json";
import { requireAgentService } from "./internal/http/service";
import { withWorkspaces } from "./internal/workspaces/bind";
import { requireWorkspaceMember } from "./internal/workspaces/member";
import { withSessions } from "./internal/sessions/bind";
import {
  connectSessionEvents,
  continueSession,
  discardSession,
  getSession,
  getSessionDiff,
  publishSession,
  startSession,
} from "./internal/sessions/handlers";
import { limitSessionOperations } from "./internal/sessions/rate-limit";

export function mapSessionEndpoints(app: ApiApp): void {
  app.post(
    "/workspaces/:workspaceId/sessions",
    withAccounts,
    requireAccount,
    withWorkspaces,
    requireWorkspaceMember,
    requireAgentService,
    limitSessionOperations,
    withSessions,
    readRequestBody,
    startSession,
  );
  app.post(
    "/workspaces/:workspaceId/sessions/:sessionId/messages",
    withAccounts,
    requireAccount,
    withWorkspaces,
    requireWorkspaceMember,
    requireAgentService,
    limitSessionOperations,
    withSessions,
    readRequestBody,
    continueSession,
  );
  app.get(
    "/workspaces/:workspaceId/sessions/:sessionId",
    withAccounts,
    requireAccount,
    withWorkspaces,
    requireWorkspaceMember,
    withSessions,
    getSession,
  );
  app.get(
    "/workspaces/:workspaceId/sessions/:sessionId/diff",
    withAccounts,
    requireAccount,
    withWorkspaces,
    requireWorkspaceMember,
    withSessions,
    getSessionDiff,
  );
  app.delete(
    "/workspaces/:workspaceId/sessions/:sessionId",
    withAccounts,
    requireAccount,
    withWorkspaces,
    requireWorkspaceMember,
    limitSessionOperations,
    withSessions,
    discardSession,
  );
  app.post(
    "/workspaces/:workspaceId/sessions/:sessionId/publish",
    withAccounts,
    requireAccount,
    withWorkspaces,
    requireWorkspaceMember,
    limitSessionOperations,
    withSessions,
    publishSession,
  );
  app.get(
    "/workspaces/:workspaceId/sessions/:sessionId/turns/:turnId/events",
    withAccounts,
    requireAccount,
    withWorkspaces,
    requireWorkspaceMember,
    withSessions,
    connectSessionEvents,
  );
}
