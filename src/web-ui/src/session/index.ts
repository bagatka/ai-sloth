import type { AuthenticatedRequest } from "@/authentication"

export type SessionStatus = "running" | "waiting" | "completed" | "failed"
export type SessionTurnStatus =
  "running" | "finalizing" | "succeeded" | "failed" | "interrupted"

export type SessionTurn = {
  id: string
  ordinal: number
  status: SessionTurnStatus
  failureCode: string | null
  resultRevision: number | null
  lastEventSequence: number
  createdAt: string
  completedAt: string | null
}

export type SessionPublication = {
  revision: number
  commitSha: string
  branch: string
  pullRequest: { number: number; url: string }
}

export type SessionDetails = {
  id: string
  name: string
  workspaceId: string
  githubRepositoryId: string
  projectId: string | null
  status: SessionStatus
  revision: number | null
  publication: SessionPublication | null
  createdAt: string
  updatedAt: string
  turns: SessionTurn[]
}

export type SessionEvent = {
  version: 1
  turnId: string
  sequence: number
  occurredAt: string
} & SessionEventPayload

export type SessionEventPayload =
  | { type: "user_message"; text: string }
  | { type: "assistant_message_started"; messageId: string }
  | {
      type: "assistant_text_delta"
      messageId: string
      contentIndex: number
      text: string
    }
  | {
      type: "assistant_message_finished"
      messageId: string
      stopReason: string
    }
  | {
      type: "tool_started"
      toolCallId: string
      toolName: string
      input: Record<string, string | number>
    }
  | {
      type: "tool_output"
      toolCallId: string
      text: string
      append: boolean
    }
  | {
      type: "tool_finished"
      toolCallId: string
      toolName: string
      isError: boolean
      diff?: string
      diffTruncated?: boolean
    }
  | {
      type: "retry_started"
      attempt: number
      maxAttempts: number
      delayMs: number
    }
  | { type: "retry_finished"; attempt: number; success: boolean }
  | { type: "compaction_started"; reason: string }
  | {
      type: "compaction_finished"
      reason: string
      success: boolean
      aborted: boolean
    }
  | { type: "agent_error"; code: string }

export type SessionSelection = { workspaceId: string; sessionId: string }

export { SessionWorkspace } from "./internal/session-workspace"
export type { AuthenticatedRequest }
