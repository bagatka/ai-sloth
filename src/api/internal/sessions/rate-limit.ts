import type { MiddlewareHandler } from "hono";
import type { ApiEnvironment } from "../environment";
import { errorResponse, HttpStatusCode } from "../http/response";

export const limitSessionOperations: MiddlewareHandler<ApiEnvironment> = async (
  context,
  next,
) => {
  const outcome = await context.env.SESSION_RATE_LIMITER.limit({
    key: context.var.account.actor.userId,
  });
  if (!outcome.success) {
    return errorResponse(
      "Too many session requests",
      HttpStatusCode.TooManyRequests,
    );
  }
  await next();
};
