import type { ISandbox } from "@cloudflare/sandbox";
import { REPOSITORY_DIR } from "./git";

const PI_SESSION_DIR = "/workspace/pi-sessions";
const PROMPT_FILE = `${PI_SESSION_DIR}/prompt.txt`;
const RESTORED_SESSION_FILE = `${PI_SESSION_DIR}/session.jsonl`;
const PI_TIMEOUT = 5 * 60 * 1000;
const PI_OUTPUT_LIMIT = 1024 * 1024;

export async function runPi(
  sandbox: ISandbox,
  prompt: string,
  priorSession?: ReadableStream<Uint8Array>,
) {
  const directory = await sandbox.mkdir(PI_SESSION_DIR, { recursive: true });
  if (!directory.success) {
    throw new Error("Could not create Pi session directory");
  }

  const promptFile = await sandbox.writeFile(PROMPT_FILE, prompt);
  if (!promptFile.success) {
    throw new Error("Could not write Pi prompt");
  }

  const piArgs = [
    "--print",
    "--no-approve",
    "--tools",
    "read,bash",
    "--session-dir",
    PI_SESSION_DIR,
  ];

  if (priorSession) {
    const restored = await sandbox.writeFile(RESTORED_SESSION_FILE, priorSession);
    if (!restored.success) {
      throw new Error("Could not restore Pi session");
    }
    piArgs.push("--session", RESTORED_SESSION_FILE);
  }

  // Pi supports piped prompts. Keep the prompt out of argv so command logs
  // cannot capture it.
  const command: [string, ...string[]] = [
    "sh",
    "-c",
    "exec pi \"$@\" < \"$PI_PROMPT_FILE\"",
    "pi",
    ...piArgs,
  ];
  const pi = await sandbox.exec(command, {
    cwd: REPOSITORY_DIR,
    env: {
      OPENROUTER_API_KEY: "injected-by-egress-proxy",
      PI_PROMPT_FILE: PROMPT_FILE,
      PI_SKIP_VERSION_CHECK: "1",
      PI_TELEMETRY: "0",
    },
    timeout: PI_TIMEOUT,
  });

  return pi.output({
    encoding: "utf8",
    maxBytes: PI_OUTPUT_LIMIT,
  });
}

export async function readPiSession(sandbox: ISandbox): Promise<{
  content: ReadableStream<Uint8Array>;
  size: number;
}> {
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
