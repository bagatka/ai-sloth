import type {
  AuthenticatedRequest,
  SessionDetails,
  SessionEvent,
  SessionPublication,
} from ".."

export async function getSessionDetails(
  request: AuthenticatedRequest,
  workspaceId: string,
  sessionId: string,
  signal: AbortSignal
): Promise<SessionDetails> {
  const response = await request(
    `/workspaces/${workspaceId}/sessions/${sessionId}`,
    { signal }
  )
  const body = await readJson(response)
  if (!response.ok) throw new Error(readError(body) ?? "Could not load session")
  if (!isSessionDetails(body)) {
    throw new Error("The server returned an invalid session")
  }
  return body
}

export async function getSessionDiff(
  request: AuthenticatedRequest,
  workspaceId: string,
  sessionId: string,
  signal: AbortSignal
): Promise<{ revision: number; patch: string }> {
  const response = await request(
    `/workspaces/${workspaceId}/sessions/${sessionId}/diff`,
    { headers: { Accept: "text/x-diff" }, signal }
  )
  if (!response.ok) {
    const body = await readJson(response)
    throw new Error(readError(body) ?? "Could not load session diff")
  }
  const revision = Number(response.headers.get("X-Session-Revision"))
  if (!Number.isSafeInteger(revision) || revision < 1) {
    throw new Error("The server returned an invalid session diff")
  }
  return { revision, patch: await response.text() }
}

export async function getSessionWorkingDiff(
  request: AuthenticatedRequest,
  workspaceId: string,
  sessionId: string,
  turnId: string,
  signal: AbortSignal
): Promise<string> {
  const response = await request(
    `/workspaces/${workspaceId}/sessions/${sessionId}/turns/${turnId}/working-diff`,
    { headers: { Accept: "text/x-diff" }, signal }
  )
  if (!response.ok) {
    const body = await readJson(response)
    throw new Error(readError(body) ?? "Could not load live working diff")
  }
  if (response.headers.get("X-Session-Turn") !== turnId) {
    throw new Error("The server returned an invalid live working diff")
  }
  return response.text()
}

export async function continueSession(
  request: AuthenticatedRequest,
  workspaceId: string,
  sessionId: string,
  prompt: string,
  signal: AbortSignal
): Promise<void> {
  const response = await request(
    `/workspaces/${workspaceId}/sessions/${sessionId}/messages`,
    {
      method: "POST",
      headers: {
        Accept: "application/json",
        "Content-Type": "application/json",
        "Idempotency-Key": crypto.randomUUID(),
      },
      body: JSON.stringify({ prompt }),
      signal,
    }
  )
  if (response.ok) return
  const body = await readJson(response)
  throw new Error(readError(body) ?? "Could not continue session")
}

export async function publishSession(
  request: AuthenticatedRequest,
  workspaceId: string,
  sessionId: string,
  signal: AbortSignal
): Promise<SessionPublication> {
  const response = await request(
    `/workspaces/${workspaceId}/sessions/${sessionId}/publish`,
    { method: "POST", signal }
  )
  const body = await readJson(response)
  if (!response.ok)
    throw new Error(readError(body) ?? "Could not publish session")
  if (!isSessionPublication(body)) {
    throw new Error("The server returned an invalid publication")
  }
  return body
}

export async function readSessionEvents(
  request: AuthenticatedRequest,
  workspaceId: string,
  sessionId: string,
  turnId: string,
  after: number,
  follow: boolean,
  signal: AbortSignal,
  accept: (event: SessionEvent) => void
): Promise<void> {
  const query = new URLSearchParams({ after: String(after) })
  if (follow) query.set("follow", "true")
  const response = await request(
    `/workspaces/${workspaceId}/sessions/${sessionId}/turns/${turnId}/events?${query}`,
    { headers: { Accept: "application/x-ndjson" }, signal }
  )
  if (!response.ok || !response.body) {
    const body = await readJson(response)
    throw new Error(readError(body) ?? "Could not load session events")
  }

  const reader = response.body.pipeThrough(new TextDecoderStream()).getReader()
  let buffer = ""
  try {
    while (true) {
      const item = await reader.read()
      if (item.done) break
      buffer += item.value
      let newline = buffer.indexOf("\n")
      while (newline !== -1) {
        const line = buffer.slice(0, newline)
        buffer = buffer.slice(newline + 1)
        if (line) accept(parseSessionEvent(line))
        newline = buffer.indexOf("\n")
      }
    }
    if (buffer) accept(parseSessionEvent(buffer))
  } finally {
    reader.releaseLock()
  }
}

function parseSessionEvent(line: string): SessionEvent {
  let value: unknown
  try {
    value = JSON.parse(line)
  } catch {
    throw new Error("The server returned an invalid session event")
  }
  if (!isSessionEvent(value)) {
    throw new Error("The server returned an invalid session event")
  }
  return value
}

function isSessionDetails(value: unknown): value is SessionDetails {
  return (
    isObject(value) &&
    typeof value.id === "string" &&
    typeof value.name === "string" &&
    typeof value.workspaceId === "string" &&
    typeof value.githubRepositoryId === "string" &&
    (value.projectId === null || typeof value.projectId === "string") &&
    (value.status === "running" ||
      value.status === "waiting" ||
      value.status === "completed" ||
      value.status === "failed") &&
    (value.revision === null || isNonNegativeInteger(value.revision)) &&
    (value.publication === null || isSessionPublication(value.publication)) &&
    typeof value.createdAt === "string" &&
    typeof value.updatedAt === "string" &&
    Array.isArray(value.turns) &&
    value.turns.every(
      (turn) =>
        isObject(turn) &&
        typeof turn.id === "string" &&
        isNonNegativeInteger(turn.ordinal) &&
        (turn.status === "running" ||
          turn.status === "finalizing" ||
          turn.status === "succeeded" ||
          turn.status === "failed" ||
          turn.status === "interrupted") &&
        (turn.failureCode === null || typeof turn.failureCode === "string") &&
        (turn.resultRevision === null ||
          isNonNegativeInteger(turn.resultRevision)) &&
        isNonNegativeInteger(turn.lastEventSequence) &&
        typeof turn.createdAt === "string" &&
        (turn.completedAt === null || typeof turn.completedAt === "string")
    )
  )
}

function isSessionPublication(value: unknown): value is SessionPublication {
  return (
    isObject(value) &&
    isNonNegativeInteger(value.revision) &&
    value.revision > 0 &&
    typeof value.commitSha === "string" &&
    typeof value.branch === "string" &&
    isObject(value.pullRequest) &&
    isNonNegativeInteger(value.pullRequest.number) &&
    value.pullRequest.number > 0 &&
    typeof value.pullRequest.url === "string"
  )
}

function isSessionEvent(value: unknown): value is SessionEvent {
  if (
    !isObject(value) ||
    value.version !== 1 ||
    typeof value.turnId !== "string" ||
    !isNonNegativeInteger(value.sequence) ||
    value.sequence < 1 ||
    typeof value.occurredAt !== "string" ||
    typeof value.type !== "string"
  ) {
    return false
  }

  switch (value.type) {
    case "user_message":
      return typeof value.text === "string"
    case "assistant_message_started":
      return typeof value.messageId === "string"
    case "assistant_text_delta":
      return (
        typeof value.messageId === "string" &&
        isNonNegativeInteger(value.contentIndex) &&
        typeof value.text === "string"
      )
    case "assistant_message_finished":
      return (
        typeof value.messageId === "string" &&
        typeof value.stopReason === "string"
      )
    case "tool_started":
      return (
        typeof value.toolCallId === "string" &&
        typeof value.toolName === "string" &&
        isScalarRecord(value.input)
      )
    case "tool_output":
      return (
        typeof value.toolCallId === "string" &&
        typeof value.text === "string" &&
        typeof value.append === "boolean"
      )
    case "tool_finished":
      return (
        typeof value.toolCallId === "string" &&
        typeof value.toolName === "string" &&
        typeof value.isError === "boolean" &&
        (value.diff === undefined || typeof value.diff === "string") &&
        (value.diffTruncated === undefined ||
          typeof value.diffTruncated === "boolean")
      )
    case "retry_started":
      return (
        isNonNegativeInteger(value.attempt) &&
        isNonNegativeInteger(value.maxAttempts) &&
        isNonNegativeInteger(value.delayMs)
      )
    case "retry_finished":
      return (
        isNonNegativeInteger(value.attempt) &&
        typeof value.success === "boolean"
      )
    case "compaction_started":
      return typeof value.reason === "string"
    case "compaction_finished":
      return (
        typeof value.reason === "string" &&
        typeof value.success === "boolean" &&
        typeof value.aborted === "boolean"
      )
    case "agent_error":
      return typeof value.code === "string"
    default:
      return false
  }
}

function isScalarRecord(
  value: unknown
): value is Record<string, string | number> {
  return (
    isObject(value) &&
    Object.values(value).every(
      (item) => typeof item === "string" || typeof item === "number"
    )
  )
}

function isNonNegativeInteger(value: unknown): value is number {
  return Number.isSafeInteger(value) && Number(value) >= 0
}

async function readJson(response: Response): Promise<unknown> {
  if (!response.headers.get("Content-Type")?.includes("application/json")) {
    return null
  }
  try {
    return await response.json()
  } catch {
    return null
  }
}

function readError(value: unknown): string | null {
  return isObject(value) && typeof value.error === "string" ? value.error : null
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value)
}
