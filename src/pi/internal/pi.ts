import type { SandboxInstance } from "@ai-sloth/sandbox";
import {
  disposeSandboxProcess,
  readSandboxProcessOutput,
} from "@ai-sloth/sandbox/process";
import type {
  ProcessLogEvent,
  SandboxProcess,
} from "@cloudflare/sandbox";

const PI_SESSION_DIR = "/workspace/pi-sessions";
const PROMPT_FILE = `${PI_SESSION_DIR}/prompt.txt`;
const PROJECT_INSTRUCTIONS_FILE = `${PI_SESSION_DIR}/project-instructions.md`;
const RESTORED_SESSION_FILE = `${PI_SESSION_DIR}/session.jsonl`;
const PI_RUNNER = "/opt/ai-sloth/pi-runner/runner.js";
const PI_TIMEOUT = 5 * 60 * 1000;
const PI_OUTPUT_LIMIT = 1024 * 1024;
const PI_ERROR_LIMIT = 64 * 1024;
const PI_EVENT_LIMIT = 10_000;
const PI_EVENT_LINE_LIMIT = 256 * 1024;
const PI_EVENT_DATA_LIMIT = 8 * 1024 * 1024;

export type PiEvent =
  | { type: "agent_started" }
  | { type: "agent_settled" }
  | { type: "turn_started" }
  | { type: "turn_finished" }
  | { type: "assistant_message_started"; messageId: string }
  | {
    type: "assistant_message_finished";
    messageId: string;
    stopReason: PiStopReason;
  }
  | {
    type: "assistant_block_started" | "assistant_block_finished";
    messageId: string;
    contentIndex: number;
    block: PiAssistantBlock;
  }
  | {
    type: "assistant_block_delta";
    messageId: string;
    contentIndex: number;
    block: PiAssistantBlock;
    text: string;
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
  | { type: "compaction_started"; reason: PiCompactionReason }
  | {
    type: "compaction_finished";
    reason: PiCompactionReason;
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

export type PiAssistantBlock = "text" | "thinking";
export type PiCompactionReason = "manual" | "threshold" | "overflow";
export type PiStopReason =
  | "stop"
  | "length"
  | "toolUse"
  | "error"
  | "aborted"
  | "deferred";

export type PiRunRequest = {
  prompt: string;
  priorSession?: ReadableStream<Uint8Array>;
  projectInstructions?: string;
  onEvent?: (event: PiEvent) => void | Promise<void>;
};

export type PiRunResult = {
  stdout: string;
  stderr: string;
  exitCode: number;
  timedOut: boolean;
  truncated: boolean;
};

export type PiSessionSnapshot = {
  content: ReadableStream<Uint8Array>;
  size: number;
};

export async function runPi(
  instance: SandboxInstance,
  request: PiRunRequest,
): Promise<PiRunResult> {
  const { sandbox } = instance;
  const { prompt, priorSession, projectInstructions } = request;
  const directory = await sandbox.mkdir(PI_SESSION_DIR, { recursive: true });
  if (!directory.success) {
    throw new Error("Could not create Pi session directory");
  }

  const promptFile = await sandbox.writeFile(PROMPT_FILE, prompt);
  if (!promptFile.success) {
    throw new Error("Could not write Pi prompt");
  }

  if (priorSession) {
    const restored = await sandbox.writeFile(RESTORED_SESSION_FILE, priorSession);
    if (!restored.success) {
      throw new Error("Could not restore Pi session");
    }
  }
  if (projectInstructions) {
    const written = await sandbox.writeFile(
      PROJECT_INSTRUCTIONS_FILE,
      projectInstructions,
    );
    if (!written.success) {
      throw new Error("Could not write project instructions");
    }
  }

  const ownership = await sandbox.exec([
    "chown",
    "-R",
    "agent:agent",
    PI_SESSION_DIR,
  ]);
  const ownershipOutput = await readSandboxProcessOutput(ownership, 4096);
  if (ownershipOutput.exitCode !== 0) {
    throw new Error("Could not prepare Pi session directory");
  }

  const pi = await sandbox.exec([
    "runuser",
    "--user",
    "agent",
    "--preserve-environment",
    "--",
    "node",
    PI_RUNNER,
  ], {
    cwd: instance.projectDirectory,
    env: {
      AI_SLOTH_PROMPT_FILE: PROMPT_FILE,
      AI_SLOTH_PROJECT_INSTRUCTIONS_FILE: projectInstructions
        ? PROJECT_INSTRUCTIONS_FILE
        : "",
      AI_SLOTH_RESTORED_SESSION_FILE: priorSession
        ? RESTORED_SESSION_FILE
        : "",
      AI_SLOTH_SESSION_DIR: PI_SESSION_DIR,
      HOME: "/home/agent",
      OPENROUTER_API_KEY: "injected-by-egress-proxy",
      PI_TELEMETRY: "0",
    },
    timeout: PI_TIMEOUT,
  });

  try {
    return await collectPiRun(pi, request.onEvent);
  } catch (error) {
    try {
      await pi.kill();
    } catch {
      // Sandbox destruction remains the final cleanup boundary.
    }
    throw error;
  } finally {
    disposeSandboxProcess(pi);
  }
}

export async function readPiSession(
  instance: SandboxInstance,
): Promise<PiSessionSnapshot> {
  const { sandbox } = instance;
  const listing = await sandbox.listFiles(PI_SESSION_DIR);
  if (!listing.success) {
    throw new Error("Could not list Pi session files");
  }

  const files = listing.files.filter(
    (file) => file.type === "file" && file.name.endsWith(".jsonl"),
  );
  if (files.length !== 1) {
    throw new Error("Pi did not produce exactly one session file");
  }

  const session = await sandbox.readFile(files[0].absolutePath, {
    encoding: "none",
  });
  return { content: session.content, size: session.size };
}

async function collectPiRun(
  process: SandboxProcess,
  onEvent?: (event: PiEvent) => void | Promise<void>,
): Promise<PiRunResult> {
  const logs = await process.logs({ replay: true, follow: true });
  const reader = logs.getReader();
  const stdoutDecoder = new TextDecoder();
  const stderrDecoder = new TextDecoder();
  const output = new BoundedText(PI_OUTPUT_LIMIT);
  const errorOutput = new BoundedText(PI_ERROR_LIMIT);
  let stdoutBuffer = "";
  let eventCount = 0;
  let eventBytes = 0;
  let exitCode: number | undefined;
  let timedOut = false;

  const acceptEvent = async (event: PiEvent): Promise<void> => {
    eventCount += 1;
    if (eventCount > PI_EVENT_LIMIT) {
      throw new Error("Pi produced too many events");
    }

    if (event.type === "assistant_block_delta" && event.block === "text") {
      const text = output.append(event.text);
      if (text.length > 0) await onEvent?.({ ...event, text });
      return;
    }
    await onEvent?.(event);
  };

  const acceptStdout = async (text: string): Promise<void> => {
    stdoutBuffer += text;
    if (stdoutBuffer.length > PI_EVENT_LINE_LIMIT && !stdoutBuffer.includes("\n")) {
      throw new Error("Pi produced an oversized event");
    }

    let newline = stdoutBuffer.indexOf("\n");
    while (newline !== -1) {
      const line = stdoutBuffer.slice(0, newline);
      stdoutBuffer = stdoutBuffer.slice(newline + 1);
      if (line.length > PI_EVENT_LINE_LIMIT) {
        throw new Error("Pi produced an oversized event");
      }
      const lineBytes = new TextEncoder().encode(line).byteLength;
      if (lineBytes > PI_EVENT_LINE_LIMIT) {
        throw new Error("Pi produced an oversized event");
      }
      eventBytes += lineBytes;
      if (eventBytes > PI_EVENT_DATA_LIMIT) {
        throw new Error("Pi produced too much event data");
      }
      if (line.length > 0) await acceptEvent(parsePiEvent(line));
      newline = stdoutBuffer.indexOf("\n");
    }
  };

  try {
    while (true) {
      const item = await reader.read();
      if (item.done) break;
      const event: ProcessLogEvent = item.value;
      if (event.type === "stdout") {
        await acceptStdout(stdoutDecoder.decode(event.data, { stream: true }));
      } else if (event.type === "stderr") {
        errorOutput.append(stderrDecoder.decode(event.data, { stream: true }));
      } else if (event.type === "truncated") {
        throw new Error("Pi process logs were truncated");
      } else if (event.type === "terminal") {
        if (event.state === "error") {
          throw new Error(`Pi process failed: ${event.error.message}`);
        }
        exitCode = event.exit.code;
        timedOut = event.exit.timedOut;
        break;
      }
    }
  } finally {
    try {
      await reader.cancel();
    } catch {
      // The sandbox process timeout and destruction still bound remote cleanup.
    }
    reader.releaseLock();
  }

  await acceptStdout(stdoutDecoder.decode());
  errorOutput.append(stderrDecoder.decode());
  if (stdoutBuffer.length > 0) {
    if (stdoutBuffer.length > PI_EVENT_LINE_LIMIT) {
      throw new Error("Pi produced an oversized event");
    }
    const lineBytes = new TextEncoder().encode(stdoutBuffer).byteLength;
    if (lineBytes > PI_EVENT_LINE_LIMIT) {
      throw new Error("Pi produced an oversized event");
    }
    eventBytes += lineBytes;
    if (eventBytes > PI_EVENT_DATA_LIMIT) {
      throw new Error("Pi produced too much event data");
    }
    await acceptEvent(parsePiEvent(stdoutBuffer));
  }
  if (exitCode === undefined) throw new Error("Pi process ended without a status");

  return {
    stdout: output.value,
    stderr: errorOutput.value,
    exitCode,
    timedOut,
    truncated: output.truncated || errorOutput.truncated,
  };
}

function parsePiEvent(line: string): PiEvent {
  let value: unknown;
  try {
    value = JSON.parse(line);
  } catch {
    throw new Error("Pi produced an invalid event");
  }
  if (!isRecord(value) || typeof value.type !== "string") {
    throw new Error("Pi produced an invalid event");
  }

  switch (value.type) {
    case "agent_started":
    case "agent_settled":
    case "turn_started":
    case "turn_finished":
      return { type: value.type };
    case "assistant_message_started":
      if (typeof value.messageId === "string") {
        return { type: value.type, messageId: value.messageId };
      }
      break;
    case "assistant_message_finished":
      if (
        typeof value.messageId === "string"
        && isStopReason(value.stopReason)
      ) {
        return {
          type: value.type,
          messageId: value.messageId,
          stopReason: value.stopReason,
        };
      }
      break;
    case "assistant_block_started":
    case "assistant_block_finished":
      if (
        typeof value.messageId === "string"
        && isNonNegativeInteger(value.contentIndex)
        && isAssistantBlock(value.block)
      ) {
        return {
          type: value.type,
          messageId: value.messageId,
          contentIndex: value.contentIndex,
          block: value.block,
        };
      }
      break;
    case "assistant_block_delta":
      if (
        typeof value.messageId === "string"
        && isNonNegativeInteger(value.contentIndex)
        && isAssistantBlock(value.block)
        && typeof value.text === "string"
      ) {
        return {
          type: value.type,
          messageId: value.messageId,
          contentIndex: value.contentIndex,
          block: value.block,
          text: value.text,
        };
      }
      break;
    case "tool_started": {
      const input = parseToolInput(value.input);
      if (
        typeof value.toolCallId === "string"
        && typeof value.toolName === "string"
        && input
      ) {
        return {
          type: value.type,
          toolCallId: value.toolCallId,
          toolName: value.toolName,
          input,
        };
      }
      break;
    }
    case "tool_output":
      if (
        typeof value.toolCallId === "string"
        && typeof value.text === "string"
        && typeof value.append === "boolean"
      ) {
        return {
          type: value.type,
          toolCallId: value.toolCallId,
          text: value.text,
          append: value.append,
        };
      }
      break;
    case "tool_finished":
      if (
        typeof value.toolCallId === "string"
        && typeof value.toolName === "string"
        && typeof value.isError === "boolean"
        && (value.diff === undefined || typeof value.diff === "string")
        && (value.diffTruncated === undefined
          || typeof value.diffTruncated === "boolean")
      ) {
        return {
          type: value.type,
          toolCallId: value.toolCallId,
          toolName: value.toolName,
          isError: value.isError,
          ...(typeof value.diff === "string" ? { diff: value.diff } : {}),
          ...(typeof value.diffTruncated === "boolean"
            ? { diffTruncated: value.diffTruncated }
            : {}),
        };
      }
      break;
    case "compaction_started":
      if (isCompactionReason(value.reason)) {
        return { type: value.type, reason: value.reason };
      }
      break;
    case "compaction_finished":
      if (
        isCompactionReason(value.reason)
        && typeof value.success === "boolean"
        && typeof value.aborted === "boolean"
      ) {
        return {
          type: value.type,
          reason: value.reason,
          success: value.success,
          aborted: value.aborted,
        };
      }
      break;
    case "retry_started":
      if (
        isNonNegativeInteger(value.attempt)
        && isNonNegativeInteger(value.maxAttempts)
        && isNonNegativeInteger(value.delayMs)
      ) {
        return {
          type: value.type,
          attempt: value.attempt,
          maxAttempts: value.maxAttempts,
          delayMs: value.delayMs,
        };
      }
      break;
    case "retry_finished":
      if (
        isNonNegativeInteger(value.attempt)
        && typeof value.success === "boolean"
      ) {
        return {
          type: value.type,
          attempt: value.attempt,
          success: value.success,
        };
      }
      break;
  }
  throw new Error("Pi produced an invalid event");
}

function parseToolInput(
  value: unknown,
): Record<string, string | number> | undefined {
  if (!isRecord(value)) return undefined;
  const entries = Object.entries(value);
  if (!entries.every(([, item]) =>
    typeof item === "string" || (typeof item === "number" && Number.isFinite(item))
  )) {
    return undefined;
  }
  return Object.fromEntries(entries) as Record<string, string | number>;
}

function isAssistantBlock(value: unknown): value is PiAssistantBlock {
  return value === "text" || value === "thinking";
}

function isCompactionReason(value: unknown): value is PiCompactionReason {
  return value === "manual" || value === "threshold" || value === "overflow";
}

function isStopReason(value: unknown): value is PiStopReason {
  return value === "stop"
    || value === "length"
    || value === "toolUse"
    || value === "error"
    || value === "aborted"
    || value === "deferred";
}

function isNonNegativeInteger(value: unknown): value is number {
  return typeof value === "number"
    && Number.isSafeInteger(value)
    && value >= 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

class BoundedText {
  readonly #chunks: string[] = [];
  readonly #limit: number;
  #bytes = 0;
  truncated = false;

  constructor(limit: number) {
    this.#limit = limit;
  }

  append(text: string): string {
    if (text.length === 0 || this.#bytes === this.#limit) {
      if (text.length > 0) this.truncated = true;
      return "";
    }

    const encoded = new TextEncoder().encode(text);
    const remaining = this.#limit - this.#bytes;
    const accepted = encoded.byteLength <= remaining
      ? text
      : new TextDecoder().decode(encoded.subarray(0, remaining));
    this.#chunks.push(accepted);
    this.#bytes += Math.min(encoded.byteLength, remaining);
    if (encoded.byteLength > remaining) this.truncated = true;
    return accepted;
  }

  get value(): string {
    return this.#chunks.join("");
  }
}
