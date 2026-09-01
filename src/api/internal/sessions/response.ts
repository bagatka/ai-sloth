import type {
  DiscardSessionOutcome,
  PublishSessionOutcome,
  SessionDetailsOutcome,
  SessionDiffOutcome,
  SessionFailure,
  SessionWorkingDiffOutcome,
  SessionOutcome,
} from "@ai-sloth/sessions";
import {
  errorResponse,
  HttpStatusCode,
} from "../http/response";

export function sessionResponse(
  outcome: SessionOutcome,
  successStatus: number,
): Response {
  return outcome.ok
    ? Response.json(outcome.value, { status: successStatus })
    : sessionFailureResponse(outcome);
}

export function sessionDetailsResponse(outcome: SessionDetailsOutcome): Response {
  return outcome.ok
    ? Response.json(outcome.value, { status: HttpStatusCode.Ok })
    : sessionFailureResponse(outcome);
}

export function sessionDiffResponse(outcome: SessionDiffOutcome): Response {
  return outcome.ok
    ? new Response(outcome.value.content, {
      status: HttpStatusCode.Ok,
      headers: {
        "Content-Type": "text/x-diff; charset=utf-8",
        "Content-Length": String(outcome.value.size),
        "Cache-Control": "private, no-store",
        "X-Content-Type-Options": "nosniff",
        "X-Session-Revision": String(outcome.value.revision),
      },
    })
    : sessionFailureResponse(outcome);
}

export function sessionWorkingDiffResponse(
  outcome: SessionWorkingDiffOutcome,
): Response {
  if (!outcome.ok) return sessionFailureResponse(outcome);
  return new Response(outcome.value.content, {
    status: HttpStatusCode.Ok,
    headers: {
      "Content-Type": "text/x-diff; charset=utf-8",
      "Content-Length": String(outcome.value.size),
      "Cache-Control": "private, no-store",
      "X-Content-Type-Options": "nosniff",
      "X-Session-Turn": outcome.value.turnId,
    },
  });
}

export function discardResponse(outcome: DiscardSessionOutcome): Response {
  return outcome.ok
    ? new Response(null, { status: HttpStatusCode.NoContent })
    : sessionFailureResponse(outcome);
}

export function publicationResponse(outcome: PublishSessionOutcome): Response {
  return outcome.ok
    ? Response.json(outcome.value, { status: HttpStatusCode.Ok })
    : sessionFailureResponse(outcome);
}

export function sessionFailureResponse(failure: SessionFailure): Response {
  switch (failure.code) {
    case "not_found":
      return errorResponse("Session not found", HttpStatusCode.NotFound);
    case "not_controller":
      return errorResponse(
        "Only the session controller may access it",
        HttpStatusCode.Forbidden,
      );
    case "github_not_connected":
      return errorResponse(
        "Connect GitHub to access this repository",
        HttpStatusCode.Conflict,
      );
    case "repository_not_found":
      return errorResponse("GitHub repository not found", HttpStatusCode.NotFound);
    case "repository_access_denied":
      return errorResponse(
        "GitHub write access is required to publish",
        HttpStatusCode.Forbidden,
      );
    case "github_unavailable":
      return errorResponse(
        "GitHub is temporarily unavailable",
        HttpStatusCode.ServiceUnavailable,
      );
    case "project_not_found":
      return errorResponse("Project not found", HttpStatusCode.NotFound);
    case "project_instructions_too_large":
      return errorResponse(
        "Combined project instructions exceed the size limit",
        HttpStatusCode.Conflict,
      );
    case "revision_limit":
      return errorResponse(
        "Session has reached its revision limit",
        HttpStatusCode.Conflict,
      );
    case "turn_limit":
      return errorResponse(
        "Session has reached its turn limit",
        HttpStatusCode.Conflict,
      );
    case "session_limit":
      return errorResponse(
        "Account has reached its session limit",
        HttpStatusCode.Conflict,
      );
    case "snapshot_too_large":
      return errorResponse(
        "Session snapshot exceeds the size limit",
        HttpStatusCode.Conflict,
      );
    case "checkpoint_too_large":
      return errorResponse(
        "Repository checkpoint exceeds the size limit",
        HttpStatusCode.Conflict,
      );
    case "transcript_too_large":
      return errorResponse(
        "Session transcript exceeds the size limit",
        HttpStatusCode.Conflict,
      );
    case "diff_not_available":
      return errorResponse(
        "A complete diff is not available for this revision",
        HttpStatusCode.Conflict,
      );
    case "working_diff_not_available":
      return errorResponse(
        "The live working diff is not available",
        HttpStatusCode.Conflict,
      );
    case "conflict":
      return errorResponse(
        "Another operation is already running for this session",
        HttpStatusCode.Conflict,
      );
    case "checkout_timeout":
      return errorResponse(
        "Repository restoration timed out",
        HttpStatusCode.GatewayTimeout,
      );
    case "checkout_failed":
      return errorResponse(
        "Repository restoration failed",
        HttpStatusCode.UnprocessableContent,
      );
    case "checkpoint_timeout":
      return errorResponse(
        "Repository checkpoint timed out",
        HttpStatusCode.GatewayTimeout,
      );
    case "checkpoint_failed":
      return errorResponse(
        "Repository checkpoint failed",
        HttpStatusCode.UnprocessableContent,
      );
    case "publication_conflict":
      return errorResponse(
        "The publication branch changed outside AI Sloth",
        HttpStatusCode.Conflict,
      );
    case "setup_timeout":
      return errorResponse(
        "Project dependency setup timed out",
        HttpStatusCode.GatewayTimeout,
      );
    case "setup_failed":
      return errorResponse(
        "Project dependency setup failed",
        HttpStatusCode.UnprocessableContent,
      );
    case "agent_timeout":
      return errorResponse("Agent timed out", HttpStatusCode.GatewayTimeout);
    case "agent_failed":
      return errorResponse("Agent failed", HttpStatusCode.InternalServerError);
    case "interrupted":
      return errorResponse("Agent execution was interrupted", 500);
    case "internal_error":
      return errorResponse(
        "Session operation failed",
        HttpStatusCode.InternalServerError,
      );
  }
}
