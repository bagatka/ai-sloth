import { expect, test } from "bun:test";
import { SessionStore } from "./session-store";

test("starts a controller-owned session at an immutable base commit", () => {
  const sessions = new SessionStore(
    {} as D1Database,
    {} as R2Bucket,
  );
  const sessionId = "b47f6e35-b7f3-4c6f-91f6-93f0479ec15b";
  const baseCommitSha = "0123456789abcdef0123456789abcdef01234567";

  const attempt = sessions.start({
    sessionId,
    turnId: "e47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
    idempotencyKey: "f47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
    workspaceId: "c47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
    controllerUserId: "d47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
    name: "Review repository",
    projectId: null,
    projectInstructions: "",
    githubRepositoryId: "1296269",
    githubUserId: "1234",
    repositoryUrl: "https://github.com/owner/repository.git",
    baseRef: "main",
    baseCommitSha,
  });

  expect(attempt).toMatchObject({
    id: sessionId,
    workspaceId: "c47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
    controllerUserId: "d47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
    name: "Review repository",
    projectId: null,
    projectInstructions: "",
    githubRepositoryId: "1296269",
    githubUserId: "1234",
    repositoryUrl: "https://github.com/owner/repository.git",
    baseRef: "main",
    baseCommitSha,
    publicationBranch: `ai-sloth/${sessionId}`,
    expectedCommitSha: "",
    revision: 1,
  });
  expect(attempt.gitObjectKey).toStartWith(`sessions/${sessionId}/00000001-`);
  expect(attempt.piObjectKey).toStartWith(`sessions/${sessionId}/00000001-`);
  expect(attempt.diffObjectKey).toBe(
    attempt.gitObjectKey.replace(/\.bundle$/, ".diff"),
  );
});

