import type { Handler } from "hono";
import type { ApiEnvironment } from "../environment";
import { errorResponse, HttpStatusCode } from "../http/response";
import {
  isSessionId,
  parseIdempotencyKey,
  parseNewSessionRequest,
  parseSessionMessageRequest,
} from "./request";
import {
  discardResponse,
  publicationResponse,
  sessionDetailsResponse,
  sessionDiffResponse,
  sessionResponse,
  sessionWorkingDiffResponse,
} from "./response";

export const startSession: Handler<ApiEnvironment> = async (context) => {
  const input = parseNewSessionRequest(context.get("requestBody"));
  const idempotencyKey = parseIdempotencyKey(
    context.req.header("Idempotency-Key"),
  );
  if (!input || !idempotencyKey) {
    return errorResponse(
      "Expected an idempotency key, GitHub repository ID, branch, and non-empty prompt",
      HttpStatusCode.BadRequest,
    );
  }

  const outcome = await context.var.sessions.start({
    sessionId: crypto.randomUUID(),
    idempotencyKey,
    workspaceId: context.var.workspace.id,
    controllerUserId: context.var.account.actor.userId,
    ...input,
  });
  return sessionResponse(outcome, HttpStatusCode.Created);
};

export const continueSession: Handler<ApiEnvironment> = async (context) => {
  const sessionId = context.req.param("sessionId") ?? "";
  const input = parseSessionMessageRequest(context.get("requestBody"));
  const idempotencyKey = parseIdempotencyKey(
    context.req.header("Idempotency-Key"),
  );
  if (!isSessionId(sessionId) || !input || !idempotencyKey) {
    return errorResponse(
      "Expected an idempotency key, valid session ID, and non-empty prompt",
      HttpStatusCode.BadRequest,
    );
  }

  const outcome = await context.var.sessions.continue({
    sessionId,
    idempotencyKey,
    workspaceId: context.var.workspace.id,
    controllerUserId: context.var.account.actor.userId,
    ...input,
  });
  return sessionResponse(outcome, HttpStatusCode.Accepted);
};

export const getSession: Handler<ApiEnvironment> = async (context) => {
  const sessionId = context.req.param("sessionId") ?? "";
  if (!isSessionId(sessionId)) {
    return errorResponse("Expected a valid session ID", HttpStatusCode.BadRequest);
  }
  return sessionDetailsResponse(await context.var.sessions.get({
    sessionId,
    workspaceId: context.var.workspace.id,
    controllerUserId: context.var.account.actor.userId,
  }));
};

export const getSessionDiff: Handler<ApiEnvironment> = async (context) => {
  const sessionId = context.req.param("sessionId") ?? "";
  if (!isSessionId(sessionId)) {
    return errorResponse("Expected a valid session ID", HttpStatusCode.BadRequest);
  }
  return sessionDiffResponse(await context.var.sessions.diff({
    sessionId,
    workspaceId: context.var.workspace.id,
    controllerUserId: context.var.account.actor.userId,
  }));
};

export const getSessionWorkingDiff: Handler<ApiEnvironment> = async (
  context,
) => {
  const sessionId = context.req.param("sessionId") ?? "";
  const turnId = context.req.param("turnId") ?? "";
  if (!isSessionId(sessionId) || !isSessionId(turnId)) {
    return errorResponse(
      "Expected valid session and turn IDs",
      HttpStatusCode.BadRequest,
    );
  }
  return sessionWorkingDiffResponse(await context.var.sessions.workingDiff({
    sessionId,
    turnId,
    workspaceId: context.var.workspace.id,
    controllerUserId: context.var.account.actor.userId,
  }));
};

export const discardSession: Handler<ApiEnvironment> = async (context) => {
  const sessionId = context.req.param("sessionId") ?? "";
  if (!isSessionId(sessionId)) {
    return errorResponse("Expected a valid session ID", HttpStatusCode.BadRequest);
  }
  const outcome = await context.var.sessions.discard({
    sessionId,
    workspaceId: context.var.workspace.id,
    controllerUserId: context.var.account.actor.userId,
  });
  return discardResponse(outcome);
};

export const publishSession: Handler<ApiEnvironment> = async (context) => {
  const sessionId = context.req.param("sessionId") ?? "";
  if (!isSessionId(sessionId)) {
    return errorResponse("Expected a valid session ID", HttpStatusCode.BadRequest);
  }
  const outcome = await context.var.sessions.publish({
    sessionId,
    workspaceId: context.var.workspace.id,
    controllerUserId: context.var.account.actor.userId,
  });
  return publicationResponse(outcome);
};

export const connectSessionEvents: Handler<ApiEnvironment> = (context) => {
  const sessionId = context.req.param("sessionId") ?? "";
  const turnId = context.req.param("turnId") ?? "";
  if (!isSessionId(sessionId) || !isSessionId(turnId)) {
    return errorResponse(
      "Expected valid session and turn IDs",
      HttpStatusCode.BadRequest,
    );
  }

  return context.var.sessions.connectEvents(
    {
      sessionId,
      workspaceId: context.var.workspace.id,
      controllerUserId: context.var.account.actor.userId,
    },
    context.req.raw,
  );
};
