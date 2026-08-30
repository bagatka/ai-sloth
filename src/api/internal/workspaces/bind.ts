import { bindWorkspaces } from "@ai-sloth/workspaces";
import type { MiddlewareHandler } from "hono";
import type { ApiEnvironment } from "../environment";

export const withWorkspaces: MiddlewareHandler<ApiEnvironment> = async (
  context,
  next,
) => {
  context.set("workspaces", bindWorkspaces(context.env));
  await next();
};
