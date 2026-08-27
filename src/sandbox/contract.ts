const MAX_BODY_BYTES = 20 * 1024;
const MAX_PROMPT_LENGTH = 16 * 1024;
const MAX_REPOSITORY_URL_LENGTH = 2048;
const MAX_BRANCH_LENGTH = 255;

export type NewSessionRequest = {
  repositoryUrl: string;
  branch: string;
  prompt: string;
};

export type SessionMessageRequest = {
  prompt: string;
};

export type SessionMessageCommand =
  | ({ kind: "new" } & NewSessionRequest)
  | ({ kind: "continue"; sessionId: string } & SessionMessageRequest);

export type SessionRunResponse = {
  sessionId: string;
  revision: number;
  commitSha: string;
  output: string;
  truncated: boolean;
};

export class RequestBodyTooLargeError extends Error {}

export async function readJsonBody(request: Request): Promise<unknown> {
  if (!request.body) {
    throw new SyntaxError("Request body is required");
  }

  const reader = request.body.getReader();
  const chunks: Uint8Array[] = [];
  let length = 0;

  while (true) {
    const { done, value } = await reader.read();
    if (done) {
      break;
    }

    length += value.byteLength;
    if (length > MAX_BODY_BYTES) {
      await reader.cancel();
      throw new RequestBodyTooLargeError();
    }
    chunks.push(value);
  }

  const body = new Uint8Array(length);
  let offset = 0;
  for (const chunk of chunks) {
    body.set(chunk, offset);
    offset += chunk.byteLength;
  }

  return JSON.parse(new TextDecoder().decode(body));
}

export function parseNewSessionRequest(value: unknown): NewSessionRequest | null {
  if (!isObject(value)) {
    return null;
  }

  const repositoryUrl = normalizeRepositoryUrl(value.repositoryUrl);
  const { branch, prompt } = value;
  if (!repositoryUrl || !isValidBranch(branch) || !isValidPrompt(prompt)) {
    return null;
  }

  return { repositoryUrl, branch, prompt };
}

export function parseSessionMessageRequest(
  value: unknown,
): SessionMessageRequest | null {
  if (!isObject(value) || !isValidPrompt(value.prompt)) {
    return null;
  }

  return { prompt: value.prompt };
}

export function isSessionId(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(
    value,
  );
}

function isObject(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value);
}

function isValidPrompt(value: unknown): value is string {
  return typeof value === "string"
    && value.trim().length > 0
    && value.length <= MAX_PROMPT_LENGTH;
}

function isValidBranch(value: unknown): value is string {
  return typeof value === "string"
    && value.trim().length > 0
    && value.length <= MAX_BRANCH_LENGTH
    && !value.startsWith("-")
    && !/[\u0000-\u001f\u007f]/.test(value);
}

function normalizeRepositoryUrl(value: unknown): string | null {
  if (typeof value !== "string" || value.length > MAX_REPOSITORY_URL_LENGTH) {
    return null;
  }

  try {
    const url = new URL(value);
    if (
      url.protocol !== "https:"
      || url.hostname !== "github.com"
      || url.port
      || url.username
      || url.password
      || url.search
      || url.hash
    ) {
      return null;
    }

    const parts = url.pathname.split("/").filter(Boolean);
    if (parts.length !== 2) {
      return null;
    }

    const owner = parts[0];
    const repository = parts[1].replace(/\.git$/, "");
    const validPart = /^[A-Za-z0-9_.-]+$/;
    if (!validPart.test(owner) || !validPart.test(repository)) {
      return null;
    }

    return `https://github.com/${owner}/${repository}.git`;
  } catch {
    return null;
  }
}
