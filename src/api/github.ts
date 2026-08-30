import type { ApiApp } from "./internal/environment";
import { requireAccount } from "./internal/accounts/authenticate";
import { withAccounts } from "./internal/accounts/bind";
import { withGitHub } from "./internal/github/bind";
import {
  completeGitHubConnection,
  disconnectGitHub,
  getGitHubConnection,
  listGitHubRepositories,
  startGitHubConnection,
} from "./internal/github/handlers";

export function mapGitHubIntegrationEndpoints(app: ApiApp): void {
  app.get("/github/callback", withGitHub, completeGitHubConnection);
  app.get(
    "/github/connection",
    withAccounts,
    requireAccount,
    withGitHub,
    getGitHubConnection,
  );
  app.post(
    "/github/connection",
    withAccounts,
    requireAccount,
    withGitHub,
    startGitHubConnection,
  );
  app.delete(
    "/github/connection",
    withAccounts,
    requireAccount,
    withGitHub,
    disconnectGitHub,
  );
  app.get(
    "/github/repositories",
    withAccounts,
    requireAccount,
    withGitHub,
    listGitHubRepositories,
  );
}
