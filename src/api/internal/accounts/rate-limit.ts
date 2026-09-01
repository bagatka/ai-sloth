import type { MiddlewareHandler } from "hono";
import type { ApiEnvironment } from "../environment";
import { errorResponse, HttpStatusCode } from "../http/response";

export const limitAuthentication: MiddlewareHandler<ApiEnvironment> = async (
  context,
  next,
) => {
  const address = context.req.header("CF-Connecting-IP") ?? "unknown";
  const email = readEmail(context.get("requestBody"));
  const key = [
    new URL(context.req.url).pathname,
    address,
    await hash(email),
  ].join(":");
  const outcome = await context.env.AUTH_RATE_LIMITER.limit({ key });
  if (!outcome.success) {
    return errorResponse(
      "Too many authentication attempts",
      HttpStatusCode.TooManyRequests,
    );
  }
  await next();
};

function readEmail(value: unknown): string {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    return "invalid";
  }
  const email = (value as Record<string, unknown>).email;
  return typeof email === "string" ? email.trim().toLowerCase() : "invalid";
}

async function hash(value: string): Promise<string> {
  const digest = await crypto.subtle.digest(
    "SHA-256",
    new TextEncoder().encode(value),
  );
  return Array.from(new Uint8Array(digest), (byte) =>
    byte.toString(16).padStart(2, "0")
  ).join("");
}
