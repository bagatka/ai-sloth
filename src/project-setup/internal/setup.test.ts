import { expect, test } from "bun:test";
import type {
  ExecOptions,
  ISandbox,
  SandboxCommand,
} from "@cloudflare/sandbox";
import type { SandboxInstance } from "@ai-sloth/sandbox";
import { prepareProject } from "../index";

test("installs locked npm dependencies without lifecycle scripts", async () => {
  const executions: Array<{ command: string[]; options?: ExecOptions }> = [];
  const instance = createSandboxInstance(executions, [
    output(),
    output(`${"a".repeat(64)}  package-lock.json\n`),
    output(),
  ]);

  await prepareProject(instance);

  expect(executions[2]?.command).toContain("--ignore-scripts");
  expect(executions[2]?.command).toContain("ci");
  expect(executions[2]?.options?.cwd).toBe("/workspace/state/project");
});

function createSandboxInstance(
  executions: Array<{ command: string[]; options?: ExecOptions }>,
  outputs: GitOutput[],
): SandboxInstance {
  const sandbox = {
    async readFile() {
      throw new Error("Not found");
    },
    async writeFile() {
      return { success: true };
    },
    async exec(command: SandboxCommand, options?: ExecOptions) {
      executions.push({ command: [...command], options });
      const next = outputs.shift();
      if (!next) throw new Error("Unexpected command");
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
