import type { WorkspaceFailureCode } from "@ai-sloth/workspaces";
import { errorResponse, HttpStatusCode } from "../http/response";

export function workspaceFailureResponse(
  code: WorkspaceFailureCode,
): Response {
  switch (code) {
    case "invalid_input":
      return errorResponse(
        "Invalid workspace input",
        HttpStatusCode.BadRequest,
      );
    case "not_found":
    case "not_member":
      return errorResponse("Workspace not found", HttpStatusCode.NotFound);
    case "last_member":
      return errorResponse(
        "A workspace must retain at least one member",
        HttpStatusCode.Conflict,
      );
    case "invalid_invitation":
      return errorResponse(
        "Workspace invitation is invalid or expired",
        HttpStatusCode.BadRequest,
      );
    case "internal_error":
      return errorResponse(
        "Workspace operation failed",
        HttpStatusCode.InternalServerError,
      );
  }
}
