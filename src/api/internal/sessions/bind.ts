import { bindSessions } from "@ai-sloth/sessions";
import type { MiddlewareHandler } from "hono";
import type { ApiEnvironment } from "../environment";

export const withSessions: MiddlewareHandler<ApiEnvironment> = async (
  context,
  next,
) => {
  context.set(
    "sessions",
    bindSessions(context.env.SESSION_COORDINATORS),
  );
  await next();
};
