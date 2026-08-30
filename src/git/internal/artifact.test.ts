import { afterEach, expect, test } from "bun:test";
import { mkdir, mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import type {
  ExecOptions,
  ISandbox,
  SandboxCommand,
} from "@cloudflare/sandbox";
import type { SandboxInstance } from "@ai-sloth/sandbox";
import {
  createRepositoryCheckpoint,
  restoreRepositoryCheckpoint,
} from "../index";

const SESSION_ID = "b47f6e35-b7f3-4c6f-91f6-93f0479ec15b";
const roots: string[] = [];

afterEach(async () => {
  await Promise.all(roots.splice(0).map((root) => rm(root, {
    recursive: true,
    force: true,
  })));
});

test("restores a self-contained checkpoint from a shallow repository", async () => {
  const root = await mkdtemp(join(tmpdir(), "ai-sloth-git-"));
  roots.push(root);
  const origin = join(root, "origin.git");
  const source = join(root, "source");
  await git(["init", "--quiet", "--bare", origin]);
  await git(["init", "--quiet", source]);
  await git(["-C", source, "config", "user.name", "Test"]);
  await git(["-C", source, "config", "user.email", "test@example.com"]);
  await Bun.write(join(source, "file.txt"), "base\n");
  await git(["-C", source, "add", "file.txt"]);
  await git(["-C", source, "commit", "--quiet", "-m", "base"]);
  await git(["-C", source, "push", "--quiet", origin, "HEAD:main"]);

  const first = localSandboxInstance(join(root, "first"));
  await mkdir(first.projectDirectory, { recursive: true });
  await git([
    "clone",
    "--quiet",
    "--depth=1",
    "--branch",
    "main",
    `--separate-git-dir=${first.gitDirectory}`,
    `file://${origin}`,
    first.projectDirectory,
  ]);
  await git([...repository(first), "config", "user.name", "Test"]);
  await git([...repository(first), "config", "user.email", "test@example.com"]);
  const baseCommitSha = await gitText([...repository(first), "rev-parse", "HEAD"]);
  await Bun.write(join(first.projectDirectory, "file.txt"), "checkpoint\n");

  const firstCheckpoint = await createRepositoryCheckpoint(first, {
    sessionId: SESSION_ID,
    revision: 1,
    baseCommitSha,
  });
  expect(firstCheckpoint.ok).toBeTrue();
  if (!firstCheckpoint.ok) return;

  await Bun.write(join(first.projectDirectory, "added.txt"), "later\n");
  const checkpoint = await createRepositoryCheckpoint(first, {
    sessionId: SESSION_ID,
    revision: 2,
    baseCommitSha,
  });
  expect(checkpoint.ok).toBeTrue();
  if (!checkpoint.ok) return;
  const diff = await new Response(checkpoint.diff.content).text();
  expect(diff).toContain("diff --git a/file.txt b/file.txt");
  expect(diff).toContain("+checkpoint");
  expect(diff).toContain("diff --git a/added.txt b/added.txt");
  expect(diff).toContain("+later");

  const restored = localSandboxInstance(join(root, "restored"));
  const result = await restoreRepositoryCheckpoint(restored, {
    baseCommitSha,
    commitSha: checkpoint.commitSha,
    artifact: checkpoint.artifact,
  });

  expect(result).toEqual({ ok: true, commitSha: checkpoint.commitSha });
  expect(await Bun.file(join(restored.projectDirectory, "file.txt")).text())
    .toBe("checkpoint\n");
  expect(await Bun.file(join(restored.projectDirectory, "added.txt")).text())
    .toBe("later\n");
  expect(await gitText([...repository(restored), "rev-parse", "HEAD^{tree}"]))
    .toBe(await gitText([...repository(first), "rev-parse", "HEAD^{tree}"]));
});

function localSandboxInstance(rootDirectory: string): SandboxInstance {
  return {
    sandbox: new LocalSandbox() as unknown as ISandbox & {
      destroy(): Promise<void>;
    },
    projectDirectory: join(rootDirectory, "project"),
    gitDirectory: join(rootDirectory, "git"),
  };
}

function repository(instance: SandboxInstance): string[] {
  return [
    `--git-dir=${instance.gitDirectory}`,
    `--work-tree=${instance.projectDirectory}`,
  ];
}

async function git(args: string[]): Promise<void> {
  const result = Bun.spawnSync(["git", ...args], {
    stdout: "pipe",
    stderr: "pipe",
  });
  if (result.exitCode !== 0) throw new Error(result.stderr.toString());
}

async function gitText(args: string[]): Promise<string> {
  const result = Bun.spawnSync(["git", ...args], {
    stdout: "pipe",
    stderr: "pipe",
  });
  if (result.exitCode !== 0) throw new Error(result.stderr.toString());
  return result.stdout.toString().trim();
}

class LocalSandbox {
  async exec(command: SandboxCommand, options: ExecOptions = {}) {
    const executable = command[0] === "/usr/local/bin/node"
      ? process.execPath
      : command[0];
    const child = Bun.spawn([executable, ...command.slice(1)], {
      cwd: options.cwd,
      env: { ...processEnv(), ...(options.env ?? {}) },
      stdout: "pipe",
      stderr: "pipe",
    });
    return {
      output: async () => {
        const [stdout, stderr, exitCode] = await Promise.all([
          new Response(child.stdout).text(),
          new Response(child.stderr).text(),
          child.exited,
        ]);
        return {
          stdout,
          stderr,
          exitCode,
          timedOut: false,
          truncated: false,
        };
      },
    };
  }

  async mkdir(path: string, options?: { recursive?: boolean }) {
    await mkdir(path, { recursive: options?.recursive });
    return { success: true };
  }

  async writeFile(path: string, content: string | ReadableStream<Uint8Array>) {
    const value = typeof content === "string"
      ? content
      : await new Response(content).arrayBuffer();
    const bytesWritten = await Bun.write(path, value);
    return { success: true, bytesWritten };
  }

  async readFile(path: string, _options: { encoding: "none" }) {
    const file = Bun.file(path);
    return { content: file.stream(), size: file.size };
  }
}

function processEnv(): Record<string, string> {
  return Object.fromEntries(
    Object.entries(process.env).filter((entry): entry is [string, string] =>
      entry[1] !== undefined
    ),
  );
}
