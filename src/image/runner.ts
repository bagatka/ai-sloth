import { readFile } from "node:fs/promises";
import { writeSync } from "node:fs";
import {
  createAgentSession,
  DefaultResourceLoader,
  SessionManager,
  type AgentSessionEvent,
} from "@earendil-works/pi-coding-agent";
import {
  captureWriteDiff,
  createWriteDiff,
  type WriteDiffSnapshot,
} from "./tool-diff.js";

const DIFF_LIMIT = 128 * 1024;
const promptFile = requireEnvironment("AI_SLOTH_PROMPT_FILE");
const sessionDirectory = requireEnvironment("AI_SLOTH_SESSION_DIR");
const restoredSessionFile = process.env.AI_SLOTH_RESTORED_SESSION_FILE;
const projectInstructionsFile = process.env.AI_SLOTH_PROJECT_INSTRUCTIONS_FILE;
const cwd = process.cwd();
const agentDir = "/home/agent/.pi/agent";
const toolOutput = new Map<string, string>();
const writeDiffs = new Map<string, WriteDiffSnapshot>();
let currentMessageId: string | undefined;
let messageSequence = 0;

const sessionManager = restoredSessionFile
  ? SessionManager.open(restoredSessionFile, sessionDirectory)
  : SessionManager.create(cwd, sessionDirectory);
const projectInstructions = projectInstructionsFile
  ? await readFile(projectInstructionsFile, "utf8")
  : "";
const resourceLoader = new DefaultResourceLoader({
  cwd,
  agentDir,
  noExtensions: true,
  noPromptTemplates: true,
  noSkills: true,
  appendSystemPrompt: projectInstructions ? [projectInstructions] : [],
});
await resourceLoader.reload();
const { session } = await createAgentSession({
  cwd,
  tools: ["read", "bash", "edit", "write"],
  resourceLoader,
  sessionManager,
});
const unsubscribe = session.subscribe(forwardEvent);

try {
  await session.prompt(await readFile(promptFile, "utf8"));
} finally {
  unsubscribe();
  session.dispose();
}

function forwardEvent(event: AgentSessionEvent): void {
  switch (event.type) {
    case "agent_start":
      emit({ type: "agent_started" });
      break;
    case "agent_settled":
      emit({ type: "agent_settled" });
      break;
    case "turn_start":
      emit({ type: "turn_started" });
      break;
    case "turn_end":
      emit({ type: "turn_finished" });
      break;
    case "message_start":
      if (event.message.role === "assistant") {
        currentMessageId = `assistant-${++messageSequence}`;
        emit({ type: "assistant_message_started", messageId: currentMessageId });
      }
      break;
    case "message_update":
      forwardAssistantUpdate(event.assistantMessageEvent);
      break;
    case "message_end":
      if (event.message.role === "assistant" && currentMessageId) {
        emit({
          type: "assistant_message_finished",
          messageId: currentMessageId,
          stopReason: event.message.stopReason,
        });
        currentMessageId = undefined;
      }
      break;
    case "tool_execution_start": {
      toolOutput.set(event.toolCallId, "");
      const writeDiff = captureWriteDiff(event.toolName, event.args, cwd);
      if (writeDiff) writeDiffs.set(event.toolCallId, writeDiff);
      emit({
        type: "tool_started",
        toolCallId: event.toolCallId,
        toolName: event.toolName,
        input: summarizeToolInput(event.toolName, event.args),
      });
      break;
    }
    case "tool_execution_update":
      forwardToolOutput(event.toolCallId, textContent(event.partialResult));
      break;
    case "tool_execution_end": {
      forwardToolOutput(event.toolCallId, textContent(event.result));
      const diff = editDiff(event.toolName, event.result)
        ?? writeDiff(event.toolCallId, event.isError);
      emit({
        type: "tool_finished",
        toolCallId: event.toolCallId,
        toolName: event.toolName,
        isError: event.isError,
        ...(diff ? { diff: diff.text, diffTruncated: diff.truncated } : {}),
      });
      toolOutput.delete(event.toolCallId);
      writeDiffs.delete(event.toolCallId);
      break;
    }
    case "compaction_start":
      emit({ type: "compaction_started", reason: event.reason });
      break;
    case "compaction_end":
      emit({
        type: "compaction_finished",
        reason: event.reason,
        success: event.result !== undefined,
        aborted: event.aborted,
      });
      break;
    case "auto_retry_start":
      emit({
        type: "retry_started",
        attempt: event.attempt,
        maxAttempts: event.maxAttempts,
        delayMs: event.delayMs,
      });
      break;
    case "auto_retry_end":
      emit({
        type: "retry_finished",
        attempt: event.attempt,
        success: event.success,
      });
      break;
  }
}

function forwardAssistantUpdate(
  event: Extract<AgentSessionEvent, { type: "message_update" }>["assistantMessageEvent"],
): void {
  if (!currentMessageId) return;

  switch (event.type) {
    case "text_start":
      emit({
        type: "assistant_block_started",
        messageId: currentMessageId,
        contentIndex: event.contentIndex,
        block: "text",
      });
      break;
    case "text_delta":
      emit({
        type: "assistant_block_delta",
        messageId: currentMessageId,
        contentIndex: event.contentIndex,
        block: "text",
        text: event.delta,
      });
      break;
    case "text_end":
      emit({
        type: "assistant_block_finished",
        messageId: currentMessageId,
        contentIndex: event.contentIndex,
        block: "text",
      });
      break;
    case "thinking_start":
      emit({
        type: "assistant_block_started",
        messageId: currentMessageId,
        contentIndex: event.contentIndex,
        block: "thinking",
      });
      break;
    case "thinking_delta":
      emit({
        type: "assistant_block_delta",
        messageId: currentMessageId,
        contentIndex: event.contentIndex,
        block: "thinking",
        text: event.delta,
      });
      break;
    case "thinking_end":
      emit({
        type: "assistant_block_finished",
        messageId: currentMessageId,
        contentIndex: event.contentIndex,
        block: "thinking",
      });
      break;
  }
}

function forwardToolOutput(toolCallId: string, output: string): void {
  const previous = toolOutput.get(toolCallId) ?? "";
  if (output === previous) return;

  const append = output.startsWith(previous);
  const text = append ? output.slice(previous.length) : output;
  if (text.length > 0) {
    emit({ type: "tool_output", toolCallId, text, append });
  }
  toolOutput.set(toolCallId, output);
}

function summarizeToolInput(toolName: string, value: unknown): ToolInput {
  if (!isRecord(value)) return {};

  switch (toolName) {
    case "bash":
      return {
        ...(typeof value.command === "string" ? { command: value.command } : {}),
        ...(typeof value.timeout === "number" ? { timeout: value.timeout } : {}),
      };
    case "read":
      return {
        ...(typeof value.path === "string" ? { path: value.path } : {}),
        ...(typeof value.offset === "number" ? { offset: value.offset } : {}),
        ...(typeof value.limit === "number" ? { limit: value.limit } : {}),
      };
    case "edit":
      return {
        ...(typeof value.path === "string" ? { path: value.path } : {}),
        ...(Array.isArray(value.edits) ? { editCount: value.edits.length } : {}),
      };
    case "write":
      return {
        ...(typeof value.path === "string" ? { path: value.path } : {}),
        ...(typeof value.content === "string"
          ? { byteCount: Buffer.byteLength(value.content) }
          : {}),
      };
    default:
      return {};
  }
}

function textContent(value: unknown): string {
  if (!isRecord(value) || !Array.isArray(value.content)) return "";
  return value.content
    .filter((item): item is { type: "text"; text: string } =>
      isRecord(item) && item.type === "text" && typeof item.text === "string"
    )
    .map((item) => item.text)
    .join("\n");
}

function editDiff(
  toolName: string,
  value: unknown,
): { text: string; truncated: boolean } | undefined {
  if (toolName !== "edit" || !isRecord(value) || !isRecord(value.details)) {
    return undefined;
  }
  if (typeof value.details.patch !== "string") return undefined;
  return truncateUtf8(value.details.patch, DIFF_LIMIT);
}

function writeDiff(
  toolCallId: string,
  isError: boolean,
): { text: string; truncated: boolean } | undefined {
  if (isError) return;
  const diff = createWriteDiff(writeDiffs.get(toolCallId));
  return diff ? truncateUtf8(diff, DIFF_LIMIT) : undefined;
}

function truncateUtf8(text: string, limit: number): { text: string; truncated: boolean } {
  const encoded = Buffer.from(text);
  if (encoded.byteLength <= limit) return { text, truncated: false };
  return { text: encoded.subarray(0, limit).toString("utf8"), truncated: true };
}

function emit(event: RunnerEvent): void {
  writeSync(process.stdout.fd, `${JSON.stringify(event)}\n`);
}

function requireEnvironment(name: string): string {
  const value = process.env[name];
  if (!value) throw new Error(`Missing ${name}`);
  return value;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

type AssistantBlock = "text" | "thinking";
type CompactionReason = "manual" | "threshold" | "overflow";
type ToolInput = Record<string, string | number>;

type RunnerEvent =
  | { type: "agent_started" }
  | { type: "agent_settled" }
  | { type: "turn_started" }
  | { type: "turn_finished" }
  | { type: "assistant_message_started"; messageId: string }
  | {
    type: "assistant_message_finished";
    messageId: string;
    stopReason: string;
  }
  | {
    type: "assistant_block_started" | "assistant_block_finished";
    messageId: string;
    contentIndex: number;
    block: AssistantBlock;
  }
  | {
    type: "assistant_block_delta";
    messageId: string;
    contentIndex: number;
    block: AssistantBlock;
    text: string;
  }
  | {
    type: "tool_started";
    toolCallId: string;
    toolName: string;
    input: ToolInput;
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
  | { type: "compaction_started"; reason: CompactionReason }
  | {
    type: "compaction_finished";
    reason: CompactionReason;
    success: boolean;
    aborted: boolean;
  }
  | {
    type: "retry_started";
    attempt: number;
    maxAttempts: number;
    delayMs: number;
  }
  | { type: "retry_finished"; attempt: number; success: boolean };
