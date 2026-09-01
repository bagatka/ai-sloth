export const SESSION_EVENT_VERSION = 1 as const;

export type SessionEvent = SessionEventIdentity & SessionEventPayload;

export type SessionEventIdentity = {
  version: typeof SESSION_EVENT_VERSION;
  turnId: string;
  sequence: number;
  occurredAt: string;
};

export type SessionEventPayload =
  | { type: "user_message"; text: string }
  | { type: "assistant_message_started"; messageId: string }
  | {
    type: "assistant_text_delta";
    messageId: string;
    contentIndex: number;
    text: string;
  }
  | {
    type: "assistant_message_finished";
    messageId: string;
    stopReason: SessionStopReason;
  }
  | {
    type: "tool_started";
    toolCallId: string;
    toolName: string;
    input: Record<string, string | number>;
  }
  | {
    type: "tool_output";
    toolCallId: string;
    text: string;
    append: boolean;
  }
  | {
    type: "tool_finished";
    toolCallId: string;
    toolName: string;
    isError: boolean;
    diff?: string;
    diffTruncated?: boolean;
  }
  | {
    type: "retry_started";
    attempt: number;
    maxAttempts: number;
    delayMs: number;
  }
  | { type: "retry_finished"; attempt: number; success: boolean }
  | { type: "compaction_started"; reason: SessionCompactionReason }
  | {
    type: "compaction_finished";
    reason: SessionCompactionReason;
    success: boolean;
    aborted: boolean;
  }
  | { type: "agent_error"; code: string };

export type SessionStopReason =
  | "stop"
  | "length"
  | "toolUse"
  | "error"
  | "aborted"
  | "deferred";

export type SessionCompactionReason = "manual" | "threshold" | "overflow";
