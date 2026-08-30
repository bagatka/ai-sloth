import type { MiddlewareHandler } from "hono";
import type { ApiEnvironment } from "../environment";
import { errorResponse, HttpStatusCode } from "../http/response";

export const requireAccount: MiddlewareHandler<ApiEnvironment> = async (
  context,
  next,
) => {
  const authorization = context.req.header("Authorization");
  const token = readBearerToken(authorization);
  if (!token) {
    console.info({
      event: "account.authentication_rejected",
      reason: authorization === undefined
        ? "missing_bearer"
        : "malformed_bearer",
    });
    return errorResponse("Unauthorized", HttpStatusCode.Unauthorized);
  }

  const outcome = await context.var.accounts.authenticate(token);
  if (!outcome.ok) {
    if (outcome.code === "invalid_session") {
      console.info({
        event: "account.authentication_rejected",
        reason: "invalid_session",
      });
    }
    return errorResponse(
      outcome.code === "internal_error"
        ? "Authentication failed"
        : "Unauthorized",
      outcome.code === "internal_error"
        ? HttpStatusCode.InternalServerError
        : HttpStatusCode.Unauthorized,
    );
  }

  context.set("account", outcome.value);
  context.set("accountToken", token);
  await next();
};

function readBearerToken(authorization: string | undefined): string | null {
  if (!authorization?.startsWith("Bearer ")) {
    return null;
  }
  const token = authorization.slice("Bearer ".length);
  return token && !token.includes(" ") ? token : null;
}
