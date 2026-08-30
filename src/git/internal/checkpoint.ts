import type { SandboxInstance } from "@ai-sloth/sandbox";
import { readSandboxProcessOutput } from "@ai-sloth/sandbox/process";
import {
  failed,
  failure,
  gitCredentialEnvironment,
  isCommit,
  isRepositoryUrl,
  repositoryArguments,
  runGit,
  type GitCredential,
  type RepositoryArtifact,
} from "./checkout";

const CHECKPOINT_TIMEOUT_MS = 2 * 60 * 1000;
const GIT_OUTPUT_LIMIT = 64 * 1024;
const AGENT_USER = "agent";
const MAX_CHANGED_FILES = 1_000;
const MAX_CHANGED_BYTES = 64 * 1024 * 1024;
const MAX_ARTIFACT_BYTES = 64 * 1024 * 1024;
const MAX_DIFF_BYTES = 16 * 1024 * 1024;
const CHECKPOINT_FILE = "/tmp/ai-sloth-checkpoint.bundle";
const CHECKPOINT_REF = "refs/ai-sloth/checkpoint";
const VALIDATE_CHECKPOINT_SCRIPT = `
const { execFileSync } = require("node:child_process");
const git = (args) => execFileSync("/usr/bin/git", args, {
  encoding: "buffer",
  env: {
    GIT_CONFIG_GLOBAL: "/dev/null",
    GIT_CONFIG_NOSYSTEM: "1",
    GIT_OPTIONAL_LOCKS: "0"
  },
  maxBuffer: 1024 * 1024
});
const repository = ["--git-dir=" + process.argv[1], "--work-tree=" + process.argv[2]];
const names = git([...repository, "diff", "--cached", "--name-only", "-z"]);
const paths = names.length === 0 ? [] : names.subarray(0, names.length - 1).toString().split("\\0");
if (paths.length > ${MAX_CHANGED_FILES}) throw new Error("Checkpoint changes too many files");
let bytes = 0;
for (const path of paths) {
  try {
    bytes += Number(git([...repository, "cat-file", "-s", ":" + path]).toString().trim());
  } catch (error) {
    if (error.status !== 128) throw error;
  }
  if (bytes > ${MAX_CHANGED_BYTES}) throw new Error("Checkpoint changes are too large");
}
`;

export type RepositoryCheckpointRequest = {
  sessionId: string;
  revision: number;
  baseCommitSha: string;
};

export type RepositoryPublishRequest = Pick<
  RepositoryCheckpointRequest,
  "sessionId" | "revision"
> & {
  repositoryUrl: string;
  branch: string;
  commitSha: string;
  expectedRemoteCommitSha: string | null;
  credential: GitCredential;
};

export type RepositoryCheckpointResult =
  | {
    ok: true;
    commitSha: string;
    artifact: RepositoryArtifact;
    diff: RepositoryArtifact;
  }
  | { ok: false; timedOut: boolean; tooLarge?: boolean; details: string };

export type RepositoryPublishResult =
  | { ok: true }
  | { ok: false; timedOut: boolean; details: string };

export async function makeRepositoryWritableByAgent(
  instance: SandboxInstance,
): Promise<void> {
  const process = await instance.sandbox.exec([
    "chown",
    "-R",
    `${AGENT_USER}:${AGENT_USER}`,
    instance.projectDirectory,
  ]);
  const output = await readSandboxProcessOutput(process, 4096);
  if (output.exitCode !== 0) {
    throw new Error("Could not prepare repository for the agent");
  }
}

export async function createRepositoryCheckpoint(
  instance: SandboxInstance,
  request: RepositoryCheckpointRequest,
): Promise<RepositoryCheckpointResult> {
  if (!isCheckpointRequest(request)) {
    throw new Error("Invalid repository checkpoint request");
  }
  const deadline = Date.now() + CHECKPOINT_TIMEOUT_MS;
  const repository = repositoryArguments(instance);
  const add = await runGit(instance, [...repository, "add", "--all"], deadline);
  if (failed(add)) return failure(add);

  const validation = await runCheckpointValidation(instance, deadline);
  if (failed(validation)) return failure(validation);

  const commit = await runGit(
    instance,
    [
      ...repository,
      "-c",
      "core.hooksPath=/dev/null",
      "commit",
      "--allow-empty",
      "--no-verify",
      "--message",
      `AI Sloth session ${request.sessionId} revision ${request.revision}`,
    ],
    deadline,
    {
      GIT_AUTHOR_NAME: "AI Sloth",
      GIT_AUTHOR_EMAIL: "agent@ai-sloth.invalid",
      GIT_COMMITTER_NAME: "AI Sloth",
      GIT_COMMITTER_EMAIL: "agent@ai-sloth.invalid",
    },
  );
  if (failed(commit)) return failure(commit);

  const head = await runGit(
    instance,
    [...repository, "rev-parse", "--verify", "HEAD^{commit}"],
    deadline,
  );
  if (failed(head)) return failure(head);
  const commitSha = head.stdout.trim();
  if (!isCommit(commitSha)) {
    return { ok: false, timedOut: false, details: "Git produced an invalid commit" };
  }

  const diff = await runGit(
    instance,
    [
      ...repository,
      "diff",
      "--no-ext-diff",
      "--no-textconv",
      "--no-color",
      "--find-renames",
      request.baseCommitSha,
      commitSha,
      "--",
    ],
    deadline,
    undefined,
    MAX_DIFF_BYTES,
  );
  if (failed(diff)) return failure(diff);
  if (diff.truncated) {
    return {
      ok: false,
      timedOut: false,
      tooLarge: true,
      details: "Repository session diff is too large",
    };
  }
  const diffSize = Buffer.byteLength(diff.stdout);

  const updateRef = await runGit(
    instance,
    [...repository, "update-ref", CHECKPOINT_REF, commitSha],
    deadline,
  );
  if (failed(updateRef)) return failure(updateRef);
  const bundle = await runGit(
    instance,
    [...repository, "bundle", "create", CHECKPOINT_FILE, CHECKPOINT_REF],
    deadline,
  );
  if (failed(bundle)) return failure(bundle);

  const artifact = await instance.sandbox.readFile(CHECKPOINT_FILE, {
    encoding: "none",
  });
  if (artifact.size > MAX_ARTIFACT_BYTES) {
    return {
      ok: false,
      timedOut: false,
      tooLarge: true,
      details: "Git checkpoint artifact is too large",
    };
  }
  return {
    ok: true,
    commitSha,
    artifact: { content: artifact.content, size: artifact.size },
    diff: { content: new Blob([diff.stdout]).stream(), size: diffSize },
  };
}

export async function publishRepositoryCheckpoint(
  instance: SandboxInstance,
  request: RepositoryPublishRequest,
): Promise<RepositoryPublishResult> {
  if (!isCheckpointIdentity(request) || !isPublishRequest(request)) {
    throw new Error("Invalid repository publication request");
  }
  const deadline = Date.now() + CHECKPOINT_TIMEOUT_MS;
  const branchRef = `refs/heads/${request.branch}`;
  const expected = request.expectedRemoteCommitSha ?? "";
  const push = await runGit(
    instance,
    [
      ...repositoryArguments(instance),
      "-c",
      "core.hooksPath=/dev/null",
      "-c",
      "credential.helper=",
      "push",
      "--no-verify",
      "--porcelain",
      `--force-with-lease=${branchRef}:${expected}`,
      "--",
      request.repositoryUrl,
      `${request.commitSha}:${branchRef}`,
    ],
    deadline,
    gitCredentialEnvironment(request.credential),
  );
  return failed(push) ? failure(push, request.credential) : { ok: true };
}

async function runCheckpointValidation(
  instance: SandboxInstance,
  deadline: number,
) {
  const process = await instance.sandbox.exec([
    "/usr/local/bin/node",
    "-e",
    VALIDATE_CHECKPOINT_SCRIPT,
    instance.gitDirectory,
    instance.projectDirectory,
  ], { timeout: Math.max(1, deadline - Date.now()) });
  return readSandboxProcessOutput(process, GIT_OUTPUT_LIMIT);
}

function isCheckpointRequest(request: RepositoryCheckpointRequest): boolean {
  return isCheckpointIdentity(request) && isCommit(request.baseCommitSha);
}

function isCheckpointIdentity(request: {
  sessionId: string;
  revision: number;
}): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(
    request.sessionId,
  )
    && Number.isSafeInteger(request.revision)
    && request.revision > 0;
}

function isPublishRequest(request: RepositoryPublishRequest): boolean {
  return isRepositoryUrl(request.repositoryUrl)
    && /^ai-sloth\/[0-9a-f-]{36}$/.test(request.branch)
    && isCommit(request.commitSha)
    && (request.expectedRemoteCommitSha === null
      || isCommit(request.expectedRemoteCommitSha))
    && request.credential.username === "x-access-token"
    && request.credential.password.length > 0;
}
