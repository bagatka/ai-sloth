import type { CreateWorkspaceInput } from "@ai-sloth/workspaces";

export function parseCreateWorkspaceRequest(
  value: unknown,
): CreateWorkspaceInput | null {
  if (!isObject(value) || typeof value.name !== "string") {
    return null;
  }
  return { name: value.name };
}

export function parseInvitationRequest(
  value: unknown,
): { invitationToken: string } | null {
  if (!isObject(value) || typeof value.invitationToken !== "string") {
    return null;
  }
  return { invitationToken: value.invitationToken };
}

function isObject(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value);
}
