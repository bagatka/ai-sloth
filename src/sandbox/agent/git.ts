import type { ISandbox } from "@cloudflare/sandbox";

export const REPOSITORY_DIR = "/workspace/repo";

const CLONE_TIMEOUT = 5 * 60 * 1000;
const CLONE_OUTPUT_LIMIT = 64 * 1024;

export async function cloneRepository(
  sandbox: ISandbox,
  repositoryUrl: string,
  branch: string,
) {
  const clone = await sandbox.exec(
    [
      "git",
      "clone",
      "--depth=1",
      "--branch",
      branch,
      "--",
      repositoryUrl,
      REPOSITORY_DIR,
    ],
    { timeout: CLONE_TIMEOUT },
  );

  return clone.output({
    encoding: "utf8",
    maxBytes: CLONE_OUTPUT_LIMIT,
  });
}
