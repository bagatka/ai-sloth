import type {
  AccountBindings,
  AccountOperations,
  AccountProfile,
} from "@ai-sloth/accounts";
import type {
  GitHubBindings,
  GitHubOperations,
} from "@ai-sloth/github";
import type {
  Workspace,
  WorkspaceBindings,
  WorkspaceOperations,
} from "@ai-sloth/workspaces";
import type {
  SessionCatalogBindings,
  SessionCatalogOperations,
  SessionCoordinatorNamespace,
  SessionOperations,
} from "@ai-sloth/sessions";
import type { Hono } from "hono";

export interface ApiBindings
  extends AccountBindings, GitHubBindings, WorkspaceBindings,
    SessionCatalogBindings {
  AUTH_RATE_LIMITER: RateLimit;
  GITHUB_APP_SLUG?: string;
  OPENROUTER_API_KEY: string;
  SESSION_COORDINATORS: SessionCoordinatorNamespace;
  SESSION_RATE_LIMITER: RateLimit;
  WEB_UI_ORIGIN?: string;
}

export type ApiEnvironment = {
  Bindings: ApiBindings;
  Variables: {
    account: AccountProfile;
    accountToken: string;
    accounts: AccountOperations;
    github: GitHubOperations;
    workspace: Workspace;
    workspaces: WorkspaceOperations;
    requestBody: unknown;
    sessionCatalog: SessionCatalogOperations;
    sessions: SessionOperations;
  };
};

export type ApiApp = Hono<ApiEnvironment>;
