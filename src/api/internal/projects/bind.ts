import { bindSessionCatalog } from "@ai-sloth/sessions";
import type { MiddlewareHandler } from "hono";
import type { ApiEnvironment } from "../environment";

export const withSessionCatalog: MiddlewareHandler<ApiEnvironment> = async (
  context,
  next,
) => {
  context.set("sessionCatalog", bindSessionCatalog(context.env));
  await next();
};
