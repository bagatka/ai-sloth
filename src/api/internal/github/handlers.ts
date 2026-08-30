import type { Handler } from "hono";
import type { ApiEnvironment } from "../environment";
import { errorResponse, HttpStatusCode } from "../http/response";
import { githubFailureResponse } from "./response";

export const startGitHubConnection: Handler<ApiEnvironment> = async (context) => {
  const callbackUrl = new URL("/github/callback", context.req.url).toString();
  const outcome = await context.var.github.startConnection(
    context.var.account.actor.userId,
    callbackUrl,
  );
  return outcome.ok
    ? Response.json(outcome.value, { status: HttpStatusCode.Created })
    : githubFailureResponse(outcome.code);
};

export const completeGitHubConnection: Handler<ApiEnvironment> = async (
  context,
) => {
  const state = context.req.query("state") ?? "";
  const code = context.req.query("code") ?? "";
  const outcome = await context.var.github.completeConnection(state, code);
  const destination = webUiDestination(
    context.env.WEB_UI_ORIGIN,
    outcome.ok ? "connected" : "failed",
  );
  if (!destination) {
    return errorResponse(
      "Web UI origin is not configured",
      HttpStatusCode.ServiceUnavailable,
    );
  }
  return Response.redirect(destination, 303);
};

export const getGitHubConnection: Handler<ApiEnvironment> = async (context) => {
  const outcome = await context.var.github.getConnection(
    context.var.account.actor.userId,
  );
  if (outcome.ok) {
    return Response.json({
      connection: outcome.value,
      installationUrl: installationUrl(context.env.GITHUB_APP_SLUG),
    });
  }
  if (outcome.code === "not_connected") {
    return Response.json({
      connection: null,
      installationUrl: installationUrl(context.env.GITHUB_APP_SLUG),
    });
  }
  return githubFailureResponse(outcome.code);
};

export const disconnectGitHub: Handler<ApiEnvironment> = async (context) => {
  const outcome = await context.var.github.disconnect(
    context.var.account.actor.userId,
  );
  return outcome.ok
    ? new Response(null, { status: 204 })
    : githubFailureResponse(outcome.code);
};

export const listGitHubRepositories: Handler<ApiEnvironment> = async (
  context,
) => {
  const outcome = await context.var.github.listRepositories(
    context.var.account.actor.userId,
    context.req.query("cursor") ?? null,
  );
  return outcome.ok
    ? Response.json(outcome.value)
    : githubFailureResponse(outcome.code);
};

function webUiDestination(
  configuredOrigin: string | undefined,
  result: "connected" | "failed",
): string | null {
  try {
    if (!configuredOrigin) return null;
    const origin = new URL(configuredOrigin);
    if (
      (origin.protocol !== "https:" && origin.protocol !== "http:")
      || !origin.hostname
      || origin.username
      || origin.password
      || origin.pathname !== "/"
      || origin.search
      || origin.hash
    ) {
      return null;
    }
    origin.searchParams.set("github", result);
    return origin.toString();
  } catch {
    return null;
  }
}

function installationUrl(slug: string | undefined): string | null {
  return slug && /^[a-z0-9-]+$/.test(slug)
    ? `https://github.com/apps/${slug}/installations/new`
    : null;
}
