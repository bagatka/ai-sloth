import type { ExecOptions } from "@cloudflare/sandbox";
import type { SandboxInstance } from "@ai-sloth/sandbox";
import { readSandboxProcessOutput } from "@ai-sloth/sandbox/process";

const CHECKOUT_TIMEOUT = 5 * 60 * 1000;
const GIT_OUTPUT_LIMIT = 64 * 1024;
const CHECKPOINT_FILE = "/tmp/ai-sloth-checkpoint.bundle";

export type GitCredential = {
  username: string;
  password: string;
};

export type RepositoryCheckoutRequest = {
  repositoryUrl: string;
  commitSha: string;
  credential: GitCredential;
};

export type RepositoryArtifact = {
  content: ReadableStream<Uint8Array>;
  size: number;
};

export type RepositoryRestoreRequest = {
  baseCommitSha: string;
  commitSha: string;
  artifact: RepositoryArtifact;
};

export type RepositoryCheckoutResult =
  | { ok: true; commitSha: string }
  | { ok: false; timedOut: boolean; details: string };

export async function checkoutRepository(
  instance: SandboxInstance,
  request: RepositoryCheckoutRequest,
): Promise<RepositoryCheckoutResult> {
  if (!isRepositoryUrl(request.repositoryUrl) || !isCommit(request.commitSha)) {
    throw new Error("Invalid repository checkout request");
  }
  const deadline = Date.now() + CHECKOUT_TIMEOUT;
  const initialization = await initializeRepository(instance, deadline);
  if (failed(initialization)) return failure(initialization);

  const fetch = await runGit(
    instance,
    [
      ...repositoryArguments(instance),
      "fetch",
      "--depth=1",
      "--no-tags",
      "--",
      request.repositoryUrl,
      request.commitSha,
    ],
    deadline,
    gitCredentialEnvironment(request.credential),
  );
  if (failed(fetch)) return failure(fetch, request.credential);

  const checkout = await runGit(
    instance,
    [
      ...repositoryArguments(instance),
      "checkout",
      "--quiet",
      "--detach",
      "FETCH_HEAD",
    ],
    deadline,
  );
  if (failed(checkout)) return failure(checkout);
  return verifyRepositoryCommit(instance, request.commitSha, deadline);
}

export async function restoreRepositoryCheckpoint(
  instance: SandboxInstance,
  request: RepositoryRestoreRequest,
): Promise<RepositoryCheckoutResult> {
  if (!isCommit(request.baseCommitSha) || !isCommit(request.commitSha)) {
    throw new Error("Invalid repository restore request");
  }
  const deadline = Date.now() + CHECKOUT_TIMEOUT;
  const prepared = await ensureRepository(instance, deadline);
  if (failed(prepared)) return failure(prepared);

  const shallow = await instance.sandbox.writeFile(
    `${instance.gitDirectory}/shallow`,
    `${request.baseCommitSha}\n`,
  );
  if (!shallow.success) {
    return { ok: false, timedOut: false, details: "Could not restore Git boundary" };
  }
  const artifact = await instance.sandbox.writeFile(
    CHECKPOINT_FILE,
    request.artifact.content,
  );
  if (!artifact.success) {
    return { ok: false, timedOut: false, details: "Could not restore Git artifact" };
  }

  const verify = await runGit(
    instance,
    [...repositoryArguments(instance), "bundle", "verify", CHECKPOINT_FILE],
    deadline,
  );
  if (failed(verify)) return failure(verify);

  const fetch = await runGit(
    instance,
    [
      ...repositoryArguments(instance),
      "fetch",
      "--force",
      "--no-tags",
      CHECKPOINT_FILE,
      "refs/ai-sloth/checkpoint:refs/ai-sloth/checkpoint",
    ],
    deadline,
  );
  if (failed(fetch)) return failure(fetch);

  const ancestry = await runGit(
    instance,
    [
      ...repositoryArguments(instance),
      "merge-base",
      "--is-ancestor",
      request.baseCommitSha,
      request.commitSha,
    ],
    deadline,
  );
  if (failed(ancestry)) {
    return {
      ok: false,
      timedOut: ancestry.timedOut,
      details: "Git checkpoint does not descend from its trusted base",
    };
  }

  const reset = await runGit(
    instance,
    [...repositoryArguments(instance), "reset", "--hard", request.commitSha],
    deadline,
  );
  if (failed(reset)) return failure(reset);
  const clean = await runGit(
    instance,
    [...repositoryArguments(instance), "clean", "-fd"],
    deadline,
  );
  if (failed(clean)) return failure(clean);
  return verifyRepositoryCommit(instance, request.commitSha, deadline);
}

export async function verifyRepositoryCommit(
  instance: SandboxInstance,
  expectedCommitSha: string,
  deadline = Date.now() + CHECKOUT_TIMEOUT,
): Promise<RepositoryCheckoutResult> {
  if (!isCommit(expectedCommitSha)) throw new Error("Invalid Git commit");
  const head = await runGit(
    instance,
    [...repositoryArguments(instance), "rev-parse", "--verify", "HEAD^{commit}"],
    deadline,
  );
  if (failed(head)) return failure(head);
  const commitSha = head.stdout.trim();
  return commitSha === expectedCommitSha
    ? { ok: true, commitSha }
    : { ok: false, timedOut: false, details: "Repository commit did not match" };
}

async function ensureRepository(instance: SandboxInstance, deadline: number) {
  const existing = await instance.sandbox.exec([
    "test",
    "-d",
    instance.gitDirectory,
  ], { timeout: Math.max(1, deadline - Date.now()) });
  const result = await readSandboxProcessOutput(existing, 4096);
  return result.exitCode === 0 ? result : initializeRepository(instance, deadline);
}

async function initializeRepository(instance: SandboxInstance, deadline: number) {
  const directory = await instance.sandbox.mkdir(instance.projectDirectory, {
    recursive: true,
  });
  if (!directory.success) {
    return commandResult("Could not create sandbox directory");
  }
  const initialized = await runGit(
    instance,
    [
      "init",
      "--quiet",
      `--separate-git-dir=${instance.gitDirectory}`,
      instance.projectDirectory,
    ],
    deadline,
  );
  if (failed(initialized)) return initialized;
  const excludes = await instance.sandbox.writeFile(
    `${instance.gitDirectory}/info/exclude`,
    "node_modules/\n",
  );
  return excludes.success
    ? initialized
    : commandResult("Could not configure disposable sandbox paths");
}

export function repositoryArguments(instance: SandboxInstance): string[] {
  return [
    `--git-dir=${instance.gitDirectory}`,
    `--work-tree=${instance.projectDirectory}`,
  ];
}

export async function runGit(
  instance: SandboxInstance,
  args: string[],
  deadline: number,
  env?: Record<string, string>,
  outputLimit = GIT_OUTPUT_LIMIT,
) {
  const options: ExecOptions = {
    timeout: Math.max(1, deadline - Date.now()),
    env: {
      GIT_CONFIG_GLOBAL: "/dev/null",
      GIT_CONFIG_NOSYSTEM: "1",
      GIT_OPTIONAL_LOCKS: "0",
      ...(env ?? {}),
    },
  };
  const process = await instance.sandbox.exec(["/usr/bin/git", ...args], options);
  return readSandboxProcessOutput(process, outputLimit);
}

export function gitCredentialEnvironment(
  credential: GitCredential,
): Record<string, string> {
  return {
    GIT_CONFIG_COUNT: "1",
    GIT_CONFIG_KEY_0: "http.https://github.com/.extraheader",
    GIT_CONFIG_VALUE_0: `Authorization: Basic ${btoa(
      `${credential.username}:${credential.password}`,
    )}`,
    GIT_TERMINAL_PROMPT: "0",
  };
}

export function failed(result: { exitCode: number; timedOut: boolean }): boolean {
  return result.exitCode !== 0 || result.timedOut;
}

export function failure(
  result: { stderr: string; timedOut: boolean },
  credential?: GitCredential,
): { ok: false; timedOut: boolean; details: string } {
  return {
    ok: false,
    timedOut: result.timedOut,
    details: credential
      ? result.stderr.replaceAll(credential.password, "[REDACTED]")
      : result.stderr,
  };
}

export function isCommit(value: string): boolean {
  return /^[0-9a-f]{40}$/.test(value);
}

export function isRepositoryUrl(value: string): boolean {
  return /^https:\/\/github\.com\/[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+\.git$/.test(
    value,
  );
}

function commandResult(stderr: string) {
  return {
    stdout: "",
    stderr,
    exitCode: 1,
    timedOut: false,
    truncated: false,
  };
}
