import type { SessionCatalogFailureCode } from "@ai-sloth/sessions";
import type { Handler } from "hono";
import type { ApiEnvironment } from "../environment";
import { errorResponse, HttpStatusCode } from "../http/response";
import {
  parseCreateProjectRequest,
  parseMoveSessionRequest,
  parseUpdateProjectRequest,
} from "./request";

export const listProjectItems: Handler<ApiEnvironment> = async (context) => {
  const outcome = await context.var.sessionCatalog.listItems({
    workspaceId: context.var.workspace.id,
    githubRepositoryId: context.req.param("repositoryId") ?? "",
    parentProjectId: context.req.query("projectId") ?? null,
    cursor: context.req.query("cursor") ?? null,
  });
  return outcome.ok
    ? Response.json(outcome.value)
    : catalogFailureResponse(outcome.code);
};

export const createProject: Handler<ApiEnvironment> = async (context) => {
  const input = parseCreateProjectRequest(context.get("requestBody"));
  if (!input) {
    return errorResponse("Expected a project name", HttpStatusCode.BadRequest);
  }
  const outcome = await context.var.sessionCatalog.createProject({
    workspaceId: context.var.workspace.id,
    githubRepositoryId: context.req.param("repositoryId") ?? "",
    ...input,
  });
  return outcome.ok
    ? Response.json(outcome.value, { status: HttpStatusCode.Created })
    : catalogFailureResponse(outcome.code);
};

export const getProject: Handler<ApiEnvironment> = async (context) => {
  const outcome = await context.var.sessionCatalog.getProject({
    workspaceId: context.var.workspace.id,
    githubRepositoryId: context.req.param("repositoryId") ?? "",
    projectId: context.req.param("projectId") ?? "",
  });
  return outcome.ok
    ? Response.json(outcome.value)
    : catalogFailureResponse(outcome.code);
};

export const updateProject: Handler<ApiEnvironment> = async (context) => {
  const input = parseUpdateProjectRequest(context.get("requestBody"));
  if (!input) {
    return errorResponse("Expected a project change", HttpStatusCode.BadRequest);
  }
  const outcome = await context.var.sessionCatalog.updateProject({
    workspaceId: context.var.workspace.id,
    githubRepositoryId: context.req.param("repositoryId") ?? "",
    projectId: context.req.param("projectId") ?? "",
    ...input,
  });
  return outcome.ok
    ? Response.json(outcome.value)
    : catalogFailureResponse(outcome.code);
};

export const moveSessionToProject: Handler<ApiEnvironment> = async (context) => {
  const input = parseMoveSessionRequest(context.get("requestBody"));
  if (!input) {
    return errorResponse("Expected a project ID", HttpStatusCode.BadRequest);
  }
  const outcome = await context.var.sessionCatalog.moveSession({
    workspaceId: context.var.workspace.id,
    githubRepositoryId: context.req.param("repositoryId") ?? "",
    sessionId: context.req.param("sessionId") ?? "",
    controllerUserId: context.var.account.actor.userId,
    ...input,
  });
  return outcome.ok
    ? new Response(null, { status: HttpStatusCode.NoContent })
    : catalogFailureResponse(outcome.code);
};

function catalogFailureResponse(code: SessionCatalogFailureCode): Response {
  switch (code) {
    case "invalid_input":
      return errorResponse("Invalid project input", HttpStatusCode.BadRequest);
    case "not_found":
      return errorResponse("Project item not found", HttpStatusCode.NotFound);
    case "not_controller":
      return errorResponse(
        "Only the session controller may move it",
        HttpStatusCode.Forbidden,
      );
    case "conflict":
      return errorResponse(
        "The project change conflicts with hierarchy or workspace limits",
        HttpStatusCode.Conflict,
      );
    case "internal_error":
      return errorResponse(
        "Project operation failed",
        HttpStatusCode.InternalServerError,
      );
  }
}
