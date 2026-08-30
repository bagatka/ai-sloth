import type { MiddlewareHandler } from "hono";
import type { ApiEnvironment } from "../environment";
import { workspaceFailureResponse } from "./response";

export const requireWorkspaceMember: MiddlewareHandler<ApiEnvironment> =
  async (context, next) => {
    const workspaceId = context.req.param("workspaceId") ?? "";
    const outcome = await context.var.workspaces.requireMember(
      context.var.account.actor,
      workspaceId,
    );
    if (!outcome.ok) {
      return workspaceFailureResponse(outcome.code);
    }

    context.set("workspace", outcome.value);
    await next();
  };
