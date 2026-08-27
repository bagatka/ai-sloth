import { runSessionMessage } from "./agent";
import {
  isSessionId,
  parseNewSessionRequest,
  parseSessionMessageRequest,
  readJsonBody,
  RequestBodyTooLargeError,
  type SessionMessageCommand,
} from "./contract";
import { errorResponse, HttpStatusCode } from "./http-status";
import type { SandboxBindings } from "./sandbox";

export { ContainerProxy } from "@cloudflare/sandbox";
export { Sandbox } from "./sandbox";

export default {
  async fetch(request: Request, env: SandboxBindings): Promise<Response> {
    const path = new URL(request.url).pathname;
    const continuation = path.match(/^\/sessions\/([^/]+)\/messages$/);
    if (
      request.method !== "POST"
      || (path !== "/sessions" && !continuation)
    ) {
      return errorResponse("Not found", HttpStatusCode.NotFound);
    }

    if (!env.SANDBOX_API_TOKEN || !env.OPENROUTER_API_KEY) {
      return errorResponse(
        "Service is not configured",
        HttpStatusCode.ServiceUnavailable,
      );
    }

    if (
      request.headers.get("Authorization") !== `Bearer ${env.SANDBOX_API_TOKEN}`
    ) {
      return errorResponse("Unauthorized", HttpStatusCode.Unauthorized);
    }

    let body: unknown;
    try {
      body = await readJsonBody(request);
    } catch (error) {
      return errorResponse(
        error instanceof RequestBodyTooLargeError
          ? "Request body is too large"
          : "Invalid JSON body",
        error instanceof RequestBodyTooLargeError
          ? HttpStatusCode.ContentTooLarge
          : HttpStatusCode.BadRequest,
      );
    }

    let command: SessionMessageCommand | null;
    if (path === "/sessions") {
      const input = parseNewSessionRequest(body);
      command = input ? { kind: "new", ...input } : null;
    } else {
      const sessionId = continuation?.[1] ?? "";
      const input = parseSessionMessageRequest(body);
      command = isSessionId(sessionId) && input
        ? { kind: "continue", sessionId, ...input }
        : null;
    }

    if (!command) {
      return errorResponse(
        path === "/sessions"
          ? "Expected a public GitHub repositoryUrl, branch, and non-empty prompt"
          : "Expected a valid session ID and non-empty prompt",
        HttpStatusCode.BadRequest,
      );
    }

    return runSessionMessage(env, command);
  },
};
