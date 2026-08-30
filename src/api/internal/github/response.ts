import type { GitHubFailureCode } from "@ai-sloth/github";
import { errorResponse, HttpStatusCode } from "../http/response";

export function githubFailureResponse(code: GitHubFailureCode): Response {
  switch (code) {
    case "invalid_input":
      return errorResponse("Invalid GitHub request", HttpStatusCode.BadRequest);
    case "not_configured":
      return errorResponse(
        "GitHub integration is not configured",
        HttpStatusCode.ServiceUnavailable,
      );
    case "not_connected":
      return errorResponse("GitHub is not connected", HttpStatusCode.Conflict);
    case "not_found":
      return errorResponse("GitHub repository not found", HttpStatusCode.NotFound);
    case "access_denied":
      return errorResponse("GitHub access was denied", HttpStatusCode.Forbidden);
    case "conflict":
      return errorResponse(
        "That GitHub account is already connected",
        HttpStatusCode.Conflict,
      );
    case "temporarily_unavailable":
      return errorResponse(
        "GitHub is temporarily unavailable",
        HttpStatusCode.ServiceUnavailable,
      );
    case "internal_error":
      return errorResponse(
        "GitHub integration failed",
        HttpStatusCode.InternalServerError,
      );
  }
}
