import { expect, test } from "bun:test";
import type {
  ExecOptions,
  ISandbox,
  ProcessLogEvent,
  SandboxCommand,
} from "@cloudflare/sandbox";
import type { SandboxInstance } from "@ai-sloth/sandbox";
import { readPiSession, runPi, type PiEvent } from "../index";

const WORKING_DIRECTORY = "/workspace/custom-repository";
const encoder = new TextEncoder();

test("runs the SDK runner in the requested directory and forwards typed events", async () => {
  const prompt = "Do not expose this prompt";
  const projectInstructions = "Follow the parent project rules";
  const writes: Array<{ path: string; content: unknown }> = [];
  const events: PiEvent[] = [];
  let execution: { command: SandboxCommand; options?: ExecOptions } | undefined;
  const sandbox = {
    mkdir: async () => ({ success: true }),
    writeFile: async (path: string, content: unknown) => {
      writes.push({ path, content });
      return { success: true };
    },
    exec: async (command: SandboxCommand, options?: ExecOptions) => {
      if (command[0] === "chown") return successfulCommand();
      execution = { command, options };
      return processWithLogs(
        stdout({ type: "agent_started" }),
        stdout({ type: "turn_started" }),
        stdout({ type: "assistant_message_started", messageId: "assistant-1" }),
        stdout({
          type: "assistant_block_started",
          messageId: "assistant-1",
          contentIndex: 0,
          block: "thinking",
        }),
        stdout({
          type: "assistant_block_delta",
          messageId: "assistant-1",
          contentIndex: 0,
          block: "thinking",
          text: "Inspect the file",
        }),
        stdout({
          type: "assistant_block_finished",
          messageId: "assistant-1",
          contentIndex: 0,
          block: "thinking",
        }),
        stdoutText('{"type":"assistant_block_delta","messageId":"assistant-1",'),
        stdoutText('"contentIndex":1,"block":"text","text":"Completed"}\n'),
        stdout({
          type: "assistant_message_finished",
          messageId: "assistant-1",
          stopReason: "toolUse",
        }),
        stdout({
          type: "tool_started",
          toolCallId: "call-1",
          toolName: "edit",
          input: { path: "src/app.ts", editCount: 1 },
        }),
        stdout({
          type: "tool_output",
          toolCallId: "call-1",
          text: "Applied edit",
          append: true,
        }),
        stdout({
          type: "tool_finished",
          toolCallId: "call-1",
          toolName: "edit",
          isError: false,
          diff: "-old\n+new",
          diffTruncated: false,
        }),
        stdout({ type: "turn_finished" }),
        stdout({
          type: "retry_started",
          attempt: 1,
          maxAttempts: 3,
          delayMs: 1_000,
        }),
        stdout({ type: "retry_finished", attempt: 1, success: true }),
        stdout({ type: "compaction_started", reason: "threshold" }),
        stdout({
          type: "compaction_finished",
          reason: "threshold",
          success: true,
          aborted: false,
        }),
        stdout({ type: "agent_settled" }),
        exited(),
      );
    },
  } as unknown as ISandbox;

  const result = await runPi(instance(sandbox), {
    prompt,
    projectInstructions,
    onEvent: (event) => {
      events.push(event);
    },
  });

  expect(result).toEqual({
    stdout: "Completed",
    stderr: "",
    exitCode: 0,
    timedOut: false,
    truncated: false,
  });
  expect(events).toEqual([
    { type: "agent_started" },
    { type: "turn_started" },
    { type: "assistant_message_started", messageId: "assistant-1" },
    {
      type: "assistant_block_started",
      messageId: "assistant-1",
      contentIndex: 0,
      block: "thinking",
    },
    {
      type: "assistant_block_delta",
      messageId: "assistant-1",
      contentIndex: 0,
      block: "thinking",
      text: "Inspect the file",
    },
    {
      type: "assistant_block_finished",
      messageId: "assistant-1",
      contentIndex: 0,
      block: "thinking",
    },
    {
      type: "assistant_block_delta",
      messageId: "assistant-1",
      contentIndex: 1,
      block: "text",
      text: "Completed",
    },
    {
      type: "assistant_message_finished",
      messageId: "assistant-1",
      stopReason: "toolUse",
    },
    {
      type: "tool_started",
      toolCallId: "call-1",
      toolName: "edit",
      input: { path: "src/app.ts", editCount: 1 },
    },
    {
      type: "tool_output",
      toolCallId: "call-1",
      text: "Applied edit",
      append: true,
    },
    {
      type: "tool_finished",
      toolCallId: "call-1",
      toolName: "edit",
      isError: false,
      diff: "-old\n+new",
      diffTruncated: false,
    },
    { type: "turn_finished" },
    {
      type: "retry_started",
      attempt: 1,
      maxAttempts: 3,
      delayMs: 1_000,
    },
    { type: "retry_finished", attempt: 1, success: true },
    { type: "compaction_started", reason: "threshold" },
    {
      type: "compaction_finished",
      reason: "threshold",
      success: true,
      aborted: false,
    },
    { type: "agent_settled" },
  ]);
  expect(writes).toContainEqual({
    path: "/workspace/pi-sessions/prompt.txt",
    content: prompt,
  });
  expect(writes).toContainEqual({
    path: "/workspace/pi-sessions/project-instructions.md",
    content: projectInstructions,
  });
  expect(execution?.options?.env?.AI_SLOTH_PROJECT_INSTRUCTIONS_FILE).toBe(
    "/workspace/pi-sessions/project-instructions.md",
  );
  expect(execution?.options?.cwd).toBe(WORKING_DIRECTORY);
  expect(execution?.command).toEqual([
    "runuser",
    "--user",
    "agent",
    "--preserve-environment",
    "--",
    "node",
    "/opt/ai-sloth/pi-runner/runner.js",
  ]);
  expect(execution?.command.join(" ")).not.toContain(prompt);
});

test("rejects output that is not a runner event", async () => {
  let killed = false;
  const sandbox = {
    mkdir: async () => ({ success: true }),
    writeFile: async () => ({ success: true }),
    exec: async (command: SandboxCommand) => command[0] === "chown"
      ? successfulCommand()
      : {
        logs: async () => eventStream({
          type: "stdout",
          cursor: "1",
          timestamp: "2026-01-01T00:00:00.000Z",
          data: encoder.encode("not-json\n"),
        }),
        kill: async () => {
          killed = true;
        },
      },
  } as unknown as ISandbox;

  await expect(runPi(instance(sandbox), {
    prompt: "Hello",
  })).rejects.toThrow("Pi produced an invalid event");
  expect(killed).toBe(true);
});

test("returns the single session snapshot produced by Pi", async () => {
  const content = new ReadableStream<Uint8Array>();
  const sandbox = {
    listFiles: async () => ({
      success: true,
      files: [
        {
          type: "file",
          name: "session.jsonl",
          absolutePath: "/workspace/pi-sessions/session.jsonl",
        },
      ],
    }),
    readFile: async () => ({ content, size: 42 }),
  } as unknown as ISandbox;

  expect(await readPiSession(instance(sandbox))).toEqual({ content, size: 42 });
});

function instance(sandbox: ISandbox): SandboxInstance {
  return {
    sandbox: sandbox as ISandbox & { destroy(): Promise<void> },
    projectDirectory: WORKING_DIRECTORY,
    gitDirectory: "/workspace/state/git",
  };
}

function successfulCommand() {
  return {
    output: async () => ({
      stdout: "",
      stderr: "",
      exitCode: 0,
      timedOut: false,
      truncated: false,
    }),
  };
}

function processWithLogs(...events: ProcessLogEvent[]) {
  return {
    logs: async () => eventStream(...events),
    kill: async () => {},
  };
}

function eventStream(...events: ProcessLogEvent[]): ReadableStream<ProcessLogEvent> {
  return new ReadableStream({
    start(controller) {
      for (const event of events) controller.enqueue(event);
      controller.close();
    },
  });
}

function stdout(event: PiEvent): ProcessLogEvent {
  return stdoutText(`${JSON.stringify(event)}\n`);
}

function stdoutText(text: string): ProcessLogEvent {
  return {
    type: "stdout",
    cursor: crypto.randomUUID(),
    timestamp: "2026-01-01T00:00:00.000Z",
    data: encoder.encode(text),
  };
}

function exited(): ProcessLogEvent {
  return {
    type: "terminal",
    state: "exited",
    cursor: crypto.randomUUID(),
    timestamp: "2026-01-01T00:00:00.000Z",
    exit: { code: 0, timedOut: false },
  };
}
