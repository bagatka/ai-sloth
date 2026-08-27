import {
  type SessionMessageCommand,
  type SessionRunResponse,
} from "../contract";
import { errorResponse, HttpStatusCode } from "../http-status";
import type { SandboxBindings } from "../sandbox";
import { runSession, SessionRunError } from "./session-run";
import { SessionStore, SessionStoreError } from "./session-store";

export async function runSessionMessage(
  env: SandboxBindings,
  command: SessionMessageCommand,
): Promise<Response> {
  const sessions = new SessionStore(env.SESSION_DB, env.SESSION_SNAPSHOTS);

  try {
    const session = command.kind === "new"
      ? sessions.start(command.repositoryUrl, command.branch)
      : await sessions.resume(command.sessionId);

    const result = await runSession(
      env.Sandbox,
      sessions,
      session,
      command.prompt,
    );

    return Response.json(
      {
        sessionId: session.id,
        revision: session.revision,
        ...result,
      } satisfies SessionRunResponse,
      {
        status: command.kind === "new"
          ? HttpStatusCode.Created
          : HttpStatusCode.Ok,
      },
    );
  } catch (error) {
    if (error instanceof SessionStoreError) {
      return sessionStoreErrorResponse(error);
    }
    if (error instanceof SessionRunError) {
      return sessionRunErrorResponse(error);
    }

    console.error(
      "Session run failed",
      error instanceof Error ? error.message : "Unknown error",
    );
    return errorResponse(
      "Session run failed",
      HttpStatusCode.InternalServerError,
    );
  }
}

function sessionStoreErrorResponse(error: SessionStoreError): Response {
  switch (error.code) {
    case "not_found":
      return errorResponse("Session not found", HttpStatusCode.NotFound);
    case "revision_limit":
      return errorResponse(
        "Session has reached its revision limit",
        HttpStatusCode.Conflict,
      );
    case "snapshot_missing":
      return errorResponse(
        "Session snapshot is missing",
        HttpStatusCode.InternalServerError,
      );
    case "stored_snapshot_too_large":
      return errorResponse(
        "Session snapshot exceeds the size limit",
        HttpStatusCode.InternalServerError,
      );
    case "snapshot_too_large":
      return errorResponse(
        "Session snapshot exceeds the size limit",
        HttpStatusCode.Conflict,
      );
    case "conflict":
      return errorResponse(
        "Another message advanced this session",
        HttpStatusCode.Conflict,
      );
  }
}

function sessionRunErrorResponse(error: SessionRunError): Response {
  switch (error.code) {
    case "checkout_timeout":
      return errorResponse(
        "Repository checkout timed out",
        HttpStatusCode.GatewayTimeout,
      );
    case "checkout_failed":
      return errorResponse(
        "Repository checkout failed",
        HttpStatusCode.UnprocessableContent,
        error.details,
      );
    case "agent_timeout":
      return errorResponse("Agent timed out", HttpStatusCode.GatewayTimeout);
    case "agent_failed":
      return errorResponse(
        "Agent failed",
        HttpStatusCode.InternalServerError,
        error.details,
      );
  }
}
