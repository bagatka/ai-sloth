import { createWorkspaceOperations } from "./internal/workspaces";
import { D1WorkspaceRepository } from "./internal/store";
import type { WorkspaceOperations } from "./workspaces";

export interface WorkspaceBindings {
  WORKSPACES_DB: D1Database;
}

export function bindWorkspaces(
  bindings: WorkspaceBindings,
): WorkspaceOperations {
  return createWorkspaceOperations(
    new D1WorkspaceRepository(bindings.WORKSPACES_DB),
  );
}
