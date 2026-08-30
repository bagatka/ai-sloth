const MAX_PROMPT_LENGTH = 16 * 1024;
const MAX_BRANCH_LENGTH = 255;
const MAX_NAME_LENGTH = 100;

export type NewSessionRequest = {
  githubRepositoryId: string;
  branch: string;
  prompt: string;
  name: string;
  projectId: string | null;
};

export type SessionMessageRequest = {
  prompt: string;
};

export function parseNewSessionRequest(value: unknown): NewSessionRequest | null {
  if (!isObject(value)) return null;

  const { githubRepositoryId, branch, prompt } = value;
  if (
    !isRepositoryId(githubRepositoryId)
    || !isValidBranch(branch)
    || !isValidPrompt(prompt)
  ) {
    return null;
  }
  const name = normalizeName(
    typeof value.name === "string" ? value.name : prompt.split("\n", 1)[0] ?? "",
  );
  const projectId = value.projectId === undefined ? null : value.projectId;
  if (!name || (projectId !== null && !isSessionId(String(projectId)))) {
    return null;
  }

  return {
    githubRepositoryId,
    branch,
    prompt,
    name,
    projectId: projectId === null ? null : String(projectId),
  };
}

export function parseSessionMessageRequest(
  value: unknown,
): SessionMessageRequest | null {
  if (!isObject(value) || !isValidPrompt(value.prompt)) return null;
  return { prompt: value.prompt };
}

export function parseIdempotencyKey(value: string | undefined): string | null {
  return value && isSessionId(value) ? value : null;
}

export function isSessionId(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(
    value,
  );
}

function isObject(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value);
}

function isRepositoryId(value: unknown): value is string {
  return typeof value === "string" && /^[1-9][0-9]{0,19}$/.test(value);
}

function isValidPrompt(value: unknown): value is string {
  return typeof value === "string"
    && value.trim().length > 0
    && value.length <= MAX_PROMPT_LENGTH;
}

function normalizeName(value: string): string | null {
  const name = value.trim().replace(/\s+/g, " ");
  return name.length > 0
    && name.length <= MAX_NAME_LENGTH
    && !/[\u0000-\u001f\u007f]/.test(name)
    ? name
    : null;
}

function isValidBranch(value: unknown): value is string {
  return typeof value === "string"
    && value.trim().length > 0
    && value.length <= MAX_BRANCH_LENGTH
    && !value.startsWith("-")
    && !/[\u0000-\u001f\u007f]/.test(value);
}
