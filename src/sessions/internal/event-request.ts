export const TRUSTED_EVENT_USER_HEADER = "X-AI-Sloth-User";
export const TRUSTED_EVENT_WORKSPACE_HEADER = "X-AI-Sloth-Workspace";

export function trustedEventRequest(
  request: Request,
  identity: { workspaceId: string; controllerUserId: string },
): Request {
  const headers = new Headers(request.headers);
  headers.set(TRUSTED_EVENT_USER_HEADER, identity.controllerUserId);
  headers.set(TRUSTED_EVENT_WORKSPACE_HEADER, identity.workspaceId);
  return new Request(request, { headers });
}
