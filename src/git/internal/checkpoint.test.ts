import { expect, test } from "bun:test";
import type {
  ExecOptions,
  ISandbox,
  SandboxCommand,
} from "@cloudflare/sandbox";
import type { SandboxInstance } from "@ai-sloth/sandbox";
import {
  createRepositoryCheckpoint,
  publishRepositoryCheckpoint,
} from "../index";

const SESSION_ID = "b47f6e35-b7f3-4c6f-91f6-93f0479ec15b";
const COMMIT_SHA = "0123456789abcdef0123456789abcdef01234567";

test("creates a self-contained platform checkpoint with hooks disabled", async () => {
  const executions: Execution[] = [];
  const instance = createSandboxInstance(executions, [
    output(),
    output(),
    output(),
    output(COMMIT_SHA),
    output("diff"),
    output(),
    output(),
  ]);

  const result = await createRepositoryCheckpoint(instance, {
    sessionId: SESSION_ID,
    revision: 2,
    baseCommitSha: COMMIT_SHA,
  });

  expect(result.ok).toBeTrue();
  if (!result.ok) return;
  expect(result.commitSha).toBe(COMMIT_SHA);
  expect(result.artifact.size).toBe(4);
  expect(result.diff.size).toBe(4);
  expect(executions[1]?.command.slice(0, 2)).toEqual([
    "/usr/local/bin/node",
    "-e",
  ]);
  expect(executions[2]?.command).toContain("core.hooksPath=/dev/null");
  expect(executions[4]?.command).toContain("diff");
  expect(executions[6]?.command).toContain("bundle");
});

test("publishes only the fixed branch with an expected-head lease", async () => {
  const executions: Execution[] = [];
  const instance = createSandboxInstance(executions, [output()]);
  const token = "secret-user-token";

  expect(await publishRepositoryCheckpoint(instance, {
    repositoryUrl: "https://github.com/owner/repository.git",
    branch: `ai-sloth/${SESSION_ID}`,
    sessionId: SESSION_ID,
    revision: 2,
    commitSha: COMMIT_SHA,
    expectedRemoteCommitSha: null,
    credential: { username: "x-access-token", password: token },
  })).toEqual({ ok: true });

  const execution = executions[0]!;
  expect(execution.command.join(" ")).not.toContain(token);
  expect(execution.command).toContain(
    `--force-with-lease=refs/heads/ai-sloth/${SESSION_ID}:`,
  );
  expect(execution.command.at(-1)).toBe(
    `${COMMIT_SHA}:refs/heads/ai-sloth/${SESSION_ID}`,
  );
});

function createSandboxInstance(
  executions: Execution[],
  outputs: GitOutput[],
): SandboxInstance {
  const sandbox = {
    async exec(command: SandboxCommand, options?: ExecOptions) {
      executions.push({ command: [...command], options });
      const next = outputs.shift();
      if (!next) throw new Error("Unexpected command");
      return { output: async () => next };
    },
    async readFile() {
      return {
        content: new Blob(["test"]).stream(),
        size: 4,
      };
    },
  } as unknown as ISandbox & { destroy(): Promise<void> };
  return {
    sandbox,
    projectDirectory: "/workspace/state/project",
    gitDirectory: "/workspace/state/git",
  };
}

function output(stdout = ""): GitOutput {
  return {
    stdout,
    stderr: "",
    exitCode: 0,
    timedOut: false,
    truncated: false,
  };
}

type Execution = { command: string[]; options?: ExecOptions };
type GitOutput = {
  stdout: string;
  stderr: string;
  exitCode: number;
  timedOut: boolean;
  truncated: boolean;
};
