import { expect, test } from "bun:test";
import type { ISandbox, SandboxCommand } from "@cloudflare/sandbox";
import type { SandboxInstance } from "@ai-sloth/sandbox";
import { checkoutRepository } from "../index";

const COMMIT_SHA = "0123456789abcdef0123456789abcdef01234567";

test("checks out an exact trusted commit in the protected instance", async () => {
  const commands: string[][] = [];
  const instance = createSandboxInstance(commands, [
    output(),
    output(),
    output(),
    output(COMMIT_SHA),
  ]);

  expect(await checkoutRepository(instance, {
    repositoryUrl: "https://github.com/owner/repository.git",
    commitSha: COMMIT_SHA,
    credential: { username: "x-access-token", password: "secret" },
  })).toEqual({ ok: true, commitSha: COMMIT_SHA });

  expect(commands[0]).toEqual([
    "/usr/bin/git",
    "init",
    "--quiet",
    "--separate-git-dir=/workspace/state/git",
    "/workspace/state/project",
  ]);
  expect(commands[1]).toContain("fetch");
  expect(commands[1]).toContain(COMMIT_SHA);
  expect(commands[2]).toContain("FETCH_HEAD");
});

function createSandboxInstance(
  commands: string[][],
  outputs: GitOutput[],
): SandboxInstance {
  const sandbox = {
    async mkdir() {
      return { success: true };
    },
    async writeFile() {
      return { success: true };
    },
    async exec(command: SandboxCommand) {
      commands.push([...command]);
      const next = outputs.shift();
      if (!next) throw new Error("Unexpected Git command");
      return { output: async () => next };
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

type GitOutput = {
  stdout: string;
  stderr: string;
  exitCode: number;
  timedOut: boolean;
  truncated: boolean;
};
