import { bindGitHub } from "@ai-sloth/github";
import type { MiddlewareHandler } from "hono";
import type { ApiEnvironment } from "../environment";

export const withGitHub: MiddlewareHandler<ApiEnvironment> = async (
  context,
  next,
) => {
  context.set("github", bindGitHub(context.env));
  await next();
};
