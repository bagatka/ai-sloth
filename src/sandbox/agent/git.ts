import type { ISandbox } from "@cloudflare/sandbox";

export const REPOSITORY_DIR = "/workspace/repo";

const CHECKOUT_TIMEOUT = 5 * 60 * 1000;
const GIT_OUTPUT_LIMIT = 64 * 1024;

type BranchSource = { repositoryUrl: string; branch: string };
type CommitSource = { repositoryUrl: string; commitSha: string };

export type RepositorySource = BranchSource | CommitSource;

export type RepositoryCheckoutResult =
  | { ok: true; commitSha: string }
  | { ok: false; timedOut: boolean; details: string };

export async function checkoutRepository(
  sandbox: ISandbox,
  source: RepositorySource,
): Promise<RepositoryCheckoutResult> {
  const deadline = Date.now() + CHECKOUT_TIMEOUT;
  const checkout = "branch" in source
    ? await cloneBranch(sandbox, source, deadline)
    : await checkoutCommit(sandbox, source, deadline);

  if (commandFailed(checkout)) {
    return checkoutFailure(checkout);
  }

  return identifyCheckedOutCommit(sandbox, deadline);
}

async function cloneBranch(
  sandbox: ISandbox,
  source: BranchSource,
  deadline: number,
) {
  return runGit(
    sandbox,
    [
      "clone",
      "--depth=1",
      "--single-branch",
      "--branch",
      source.branch,
      "--",
      source.repositoryUrl,
      REPOSITORY_DIR,
    ],
    deadline,
  );
}

async function checkoutCommit(
  sandbox: ISandbox,
  source: CommitSource,
  deadline: number,
) {
  const initialization = await initializeRepository(sandbox, deadline);
  if (commandFailed(initialization)) {
    return initialization;
  }

  const fetch = await fetchCommit(sandbox, source, deadline);
  if (commandFailed(fetch)) {
    return fetch;
  }

  return checkoutFetchedCommit(sandbox, deadline);
}

async function initializeRepository(sandbox: ISandbox, deadline: number) {
  return runGit(sandbox, ["init", "--quiet", REPOSITORY_DIR], deadline);
}

async function fetchCommit(
  sandbox: ISandbox,
  source: CommitSource,
  deadline: number,
) {
  return runGit(
    sandbox,
    [
      "-C",
      REPOSITORY_DIR,
      "fetch",
      "--depth=1",
      "--no-tags",
      "--",
      source.repositoryUrl,
      source.commitSha,
    ],
    deadline,
  );
}

async function checkoutFetchedCommit(sandbox: ISandbox, deadline: number) {
  return runGit(
    sandbox,
    ["-C", REPOSITORY_DIR, "checkout", "--quiet", "--detach", "FETCH_HEAD"],
    deadline,
  );
}

async function identifyCheckedOutCommit(
  sandbox: ISandbox,
  deadline: number,
): Promise<RepositoryCheckoutResult> {
  const head = await runGit(
    sandbox,
    ["-C", REPOSITORY_DIR, "rev-parse", "--verify", "HEAD^{commit}"],
    deadline,
  );
  if (commandFailed(head)) {
    return checkoutFailure(head);
  }

  return { ok: true, commitSha: head.stdout.trim() };
}

async function runGit(
  sandbox: ISandbox,
  args: string[],
  deadline: number,
) {
  const process = await sandbox.exec(["git", ...args], {
    timeout: Math.max(1, deadline - Date.now()),
  });
  return process.output({
    encoding: "utf8",
    maxBytes: GIT_OUTPUT_LIMIT,
  });
}

function commandFailed(
  result: { timedOut: boolean; exitCode: number },
): boolean {
  return result.timedOut || result.exitCode !== 0;
}

function checkoutFailure(
  result: { timedOut: boolean; stderr: string },
): RepositoryCheckoutResult {
  return {
    ok: false,
    timedOut: result.timedOut,
    details: result.stderr,
  };
}
