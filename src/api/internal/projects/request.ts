export type CreateProjectRequest = {
  name: string;
  parentProjectId: string | null;
};

export type UpdateProjectRequest = {
  name?: string;
  instructions?: string;
  expectedVersion?: number;
  parentProjectId?: string | null;
};

export function parseCreateProjectRequest(
  value: unknown,
): CreateProjectRequest | null {
  if (!isObject(value) || typeof value.name !== "string") return null;
  const parentProjectId = value.parentProjectId ?? null;
  return parentProjectId === null || typeof parentProjectId === "string"
    ? { name: value.name, parentProjectId }
    : null;
}

export function parseUpdateProjectRequest(
  value: unknown,
): UpdateProjectRequest | null {
  if (!isObject(value)) return null;
  const result: UpdateProjectRequest = {};
  if ("name" in value) {
    if (typeof value.name !== "string") return null;
    result.name = value.name;
  }
  if ("instructions" in value) {
    if (typeof value.instructions !== "string") return null;
    result.instructions = value.instructions;
  }
  if ("expectedVersion" in value) {
    if (typeof value.expectedVersion !== "number") return null;
    result.expectedVersion = value.expectedVersion;
  }
  if ("parentProjectId" in value) {
    if (value.parentProjectId !== null && typeof value.parentProjectId !== "string") {
      return null;
    }
    result.parentProjectId = value.parentProjectId;
  }
  return Object.keys(result).length > 0 ? result : null;
}

export function parseMoveSessionRequest(
  value: unknown,
): { projectId: string | null } | null {
  if (
    !isObject(value)
    || !("projectId" in value)
    || (value.projectId !== null && typeof value.projectId !== "string")
  ) {
    return null;
  }
  return { projectId: value.projectId };
}

function isObject(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value);
}
