import type { Handler } from "hono";
import type { ApiEnvironment } from "../environment";
import { errorResponse, HttpStatusCode } from "../http/response";
import {
  parseCreateWorkspaceRequest,
  parseInvitationRequest,
} from "./request";
import { workspaceFailureResponse } from "./response";

export const createWorkspace: Handler<ApiEnvironment> = async (context) => {
  const input = parseCreateWorkspaceRequest(context.get("requestBody"));
  if (!input) {
    return errorResponse(
      "Expected a workspace name",
      HttpStatusCode.BadRequest,
    );
  }

  const outcome = await context.var.workspaces.create(
    context.var.account.actor,
    input,
  );
  return outcome.ok
    ? Response.json(outcome.value, { status: HttpStatusCode.Created })
    : workspaceFailureResponse(outcome.code);
};

export const listWorkspaces: Handler<ApiEnvironment> = async (context) => {
  const outcome = await context.var.workspaces.list(
    context.var.account.actor,
  );
  return outcome.ok
    ? Response.json({ workspaces: outcome.value })
    : workspaceFailureResponse(outcome.code);
};

export const listMembers: Handler<ApiEnvironment> = async (context) => {
  const outcome = await context.var.workspaces.listMembers(
    context.var.account.actor,
    context.req.param("workspaceId") ?? "",
  );
  return outcome.ok
    ? Response.json({ members: outcome.value })
    : workspaceFailureResponse(outcome.code);
};

export const createInvitation: Handler<ApiEnvironment> = async (context) => {
  const outcome = await context.var.workspaces.createInvitation(
    context.var.account.actor,
    context.req.param("workspaceId") ?? "",
  );
  return outcome.ok
    ? Response.json(outcome.value, {
        status: HttpStatusCode.Created,
        headers: { "Cache-Control": "no-store" },
      })
    : workspaceFailureResponse(outcome.code);
};

export const acceptInvitation: Handler<ApiEnvironment> = async (context) => {
  const input = parseInvitationRequest(context.get("requestBody"));
  if (!input) {
    return errorResponse(
      "Expected an invitation token",
      HttpStatusCode.BadRequest,
    );
  }

  const outcome = await context.var.workspaces.acceptInvitation(
    context.var.account.actor,
    input.invitationToken,
  );
  return outcome.ok
    ? Response.json(outcome.value)
    : workspaceFailureResponse(outcome.code);
};

export const removeMember: Handler<ApiEnvironment> = async (context) => {
  const outcome = await context.var.workspaces.removeMember(
    context.var.account.actor,
    context.req.param("workspaceId") ?? "",
    context.req.param("userId") ?? "",
  );
  return outcome.ok
    ? new Response(null, { status: 204 })
    : workspaceFailureResponse(outcome.code);
};
