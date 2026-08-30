import type { MiddlewareHandler } from "hono";
import type { ApiEnvironment } from "../environment";
import { errorResponse, HttpStatusCode } from "./response";

export const requireAgentService: MiddlewareHandler<ApiEnvironment> = async (
  context,
  next,
) => {
  if (!context.env.OPENROUTER_API_KEY) {
    return errorResponse(
      "Service is not configured",
      HttpStatusCode.ServiceUnavailable,
    );
  }
  await next();
};
