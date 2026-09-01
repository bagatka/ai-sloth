import { Hono } from "hono";
import { mapAuthenticationEndpoints } from "./authentication";
import { mapGitHubIntegrationEndpoints } from "./github";
import { mapProjectEndpoints } from "./projects";
import type { ApiEnvironment } from "./internal/environment";
import { mapWorkspaceEndpoints } from "./workspaces";
import { mapSessionEndpoints } from "./sessions";

export function createApp() {
  const app = new Hono<ApiEnvironment>();

  mapAuthenticationEndpoints(app);
  mapWorkspaceEndpoints(app);
  mapGitHubIntegrationEndpoints(app);
  mapProjectEndpoints(app);
  mapSessionEndpoints(app);

  app.notFound((context) => context.json({ error: "Not found" }, 404));

  return app;
}
