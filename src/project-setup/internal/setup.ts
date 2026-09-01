import type { SandboxInstance } from "@ai-sloth/sandbox";
import { readSandboxProcessOutput } from "@ai-sloth/sandbox/process";

const SETUP_TIMEOUT_MS = 10 * 60 * 1000;
const SETUP_OUTPUT_LIMIT = 64 * 1024;

export const PROJECT_SETUP_VERSION = "npm-ci-ignore-scripts-v1";

export async function prepareProject(instance: SandboxInstance): Promise<void> {
  const packageLockPath = `${instance.projectDirectory}/package-lock.json`;
  const packageLock = await instance.sandbox.exec([
    "test",
    "-f",
    packageLockPath,
  ]);
  const exists = await readSandboxProcessOutput(packageLock, 4096);
  if (exists.exitCode !== 0) return;

  const digestProcess = await instance.sandbox.exec([
    "sha256sum",
    packageLockPath,
  ]);
  const digestOutput = await readSandboxProcessOutput(digestProcess, 4096);
  const digest = digestOutput.stdout.split(" ", 1)[0] ?? "";
  if (digestOutput.exitCode !== 0 || !/^[0-9a-f]{64}$/.test(digest)) {
    throw new ProjectSetupError("setup_failed");
  }

  const markerPath = `${instance.gitDirectory}/ai-sloth-project-setup`;
  const marker = `${PROJECT_SETUP_VERSION}:${digest}\n`;
  try {
    const stored = await instance.sandbox.readFile(markerPath, {
      encoding: "utf8",
    });
    if (stored.content === marker) return;
  } catch {
    // A missing marker is a normal cold setup.
  }

  const setup = await instance.sandbox.exec([
    "runuser",
    "--user",
    "agent",
    "--preserve-environment",
    "--",
    "npm",
    "ci",
    "--ignore-scripts",
    "--no-audit",
    "--no-fund",
  ], {
    cwd: instance.projectDirectory,
    env: {
      HOME: "/home/agent",
      npm_config_update_notifier: "false",
    },
    timeout: SETUP_TIMEOUT_MS,
  });
  const output = await readSandboxProcessOutput(setup, SETUP_OUTPUT_LIMIT);
  if (output.timedOut) throw new ProjectSetupError("setup_timeout");
  if (output.exitCode !== 0) {
    throw new ProjectSetupError("setup_failed", output.stderr);
  }
  const saved = await instance.sandbox.writeFile(markerPath, marker);
  if (!saved.success) throw new ProjectSetupError("setup_failed");
}

export class ProjectSetupError extends Error {
  constructor(
    readonly code: "setup_timeout" | "setup_failed",
    readonly details?: string,
  ) {
    super(code);
  }
}
