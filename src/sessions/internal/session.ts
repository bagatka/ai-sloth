import {
  publishRepositoryCheckpoint,
  restoreRepositoryCheckpoint,
  type RepositoryCheckoutResult,
  type RepositoryPublishResult,
} from "@ai-sloth/git";
import type {
  GitHubOperations,
  GitHubRepositoryAccess,
} from "@ai-sloth/github";
import {
  createSandboxInstance,
  deleteSandboxBackup,
  destroySandboxInstance,
} from "@ai-sloth/sandbox";
import type {
  ContinueSessionInput,
  DiscardSessionInput,
  DiscardSessionOutcome,
  GetSessionInput,
  PublishSessionInput,
  PublishSessionOutcome,
  SessionAccepted,
  SessionDetailsOutcome,
  SessionDiffOutcome,
  SessionFailure,
  SessionOutcome,
  SessionResources,
  StartSessionInput,
} from "./contract";
import {
  ProjectContextError,
  resolveProjectInstructions,
} from "./catalog";
import { EventLogError, type TurnEventLog } from "./event-log";
import { runSession, SessionRunError } from "./session-run";
import {
  SessionStore,
  SessionStoreError,
  type SessionAttempt,
} from "./session-store";

export type PreparedSessionTurn = {
  accepted: SessionAccepted;
  attempt?: SessionAttempt;
  prompt?: string;
  repository?: GitHubRepositoryAccess;
};

export async function prepareStartSession(
  resources: SessionResources,
  input: StartSessionInput,
): Promise<SessionOutcome<PreparedSessionTurn>> {
  const sessions = new SessionStore(resources.database, resources.artifacts);

  try {
    const existing = await sessions.findAcceptedTurn(
      input.idempotencyKey,
      input.workspaceId,
      input.controllerUserId,
    );
    if (existing) return { ok: true, value: { accepted: existing } };

    const repository = await requireRepositoryAccess(
      resources.github,
      input.controllerUserId,
      input.githubRepositoryId,
      undefined,
      false,
    );
    const head = await resources.github.getBranchHead(
      input.controllerUserId,
      input.githubRepositoryId,
      input.branch,
      repository.githubUserId,
    );
    if (!head.ok) throw new RepositoryAccessError(head.code);
    if (head.value === null) throw new RepositoryAccessError("not_found");

    const projectInstructions = await resolveProjectInstructions(
      resources.database,
      {
        workspaceId: input.workspaceId,
        githubRepositoryId: input.githubRepositoryId,
        projectId: input.projectId,
      },
    );
    const attempt = sessions.start({
      sessionId: input.sessionId,
      turnId: crypto.randomUUID(),
      idempotencyKey: input.idempotencyKey,
      workspaceId: input.workspaceId,
      controllerUserId: input.controllerUserId,
      name: input.name,
      projectId: input.projectId,
      projectInstructions,
      githubRepositoryId: input.githubRepositoryId,
      githubUserId: repository.githubUserId,
      repositoryUrl: repository.cloneUrl,
      baseRef: input.branch,
      baseCommitSha: head.value,
    });
    await sessions.reserve(attempt);
    return {
      ok: true,
      value: {
        accepted: accepted(attempt),
        attempt,
        prompt: input.prompt,
        repository,
      },
    };
  } catch (error) {
    if (error instanceof SessionStoreError && error.code === "conflict") {
      const existing = await sessions.findAcceptedTurn(
        input.idempotencyKey,
        input.workspaceId,
        input.controllerUserId,
      );
      if (existing) return { ok: true, value: { accepted: existing } };
    }
    return sessionFailure(error);
  }
}

export async function prepareContinuedSession(
  resources: SessionResources,
  input: ContinueSessionInput,
): Promise<SessionOutcome<PreparedSessionTurn>> {
  const sessions = new SessionStore(resources.database, resources.artifacts);

  try {
    const existing = await sessions.findAcceptedTurn(
      input.idempotencyKey,
      input.workspaceId,
      input.controllerUserId,
    );
    if (existing) {
      return existing.sessionId === input.sessionId
        ? { ok: true, value: { accepted: existing } }
        : { ok: false, code: "conflict" };
    }

    const attempt = await sessions.resume(
      input.sessionId,
      crypto.randomUUID(),
      input.idempotencyKey,
      input.workspaceId,
      input.controllerUserId,
    );
    const repository = attempt.previousRevision
      ? undefined
      : await requireRepositoryAccess(
        resources.github,
        input.controllerUserId,
        attempt.githubRepositoryId,
        attempt.githubUserId,
        false,
      );
    await sessions.reserve(attempt);
    return {
      ok: true,
      value: {
        accepted: accepted(attempt),
        attempt,
        prompt: input.prompt,
        repository,
      },
    };
  } catch (error) {
    if (error instanceof SessionStoreError && error.code === "conflict") {
      const existing = await sessions.findAcceptedTurn(
        input.idempotencyKey,
        input.workspaceId,
        input.controllerUserId,
      );
      if (existing?.sessionId === input.sessionId) {
        return { ok: true, value: { accepted: existing } };
      }
    }
    return sessionFailure(error);
  }
}

export async function executePreparedTurn(
  resources: SessionResources,
  prepared: Required<Pick<PreparedSessionTurn, "attempt" | "prompt">>
    & Pick<PreparedSessionTurn, "repository">,
  events: TurnEventLog,
): Promise<void> {
  const sessions = new SessionStore(resources.database, resources.artifacts);
  await runSession(
    resources,
    sessions,
    prepared.attempt,
    prepared.prompt,
    events,
    prepared.repository,
  );
}

export async function failPreparedTurn(
  resources: SessionResources,
  attempt: SessionAttempt,
  events: TurnEventLog,
  error: unknown,
): Promise<SessionFailure> {
  const failure = sessionFailure(error);
  let transcript;
  try {
    await events.appendError(failure.code);
    transcript = await events.finish();
  } catch {
    try {
      transcript = await events.snapshot();
    } catch {
      // A journal storage failure means no event was delivered from that batch.
    }
  }

  try {
    await new SessionStore(resources.database, resources.artifacts).fail(
      attempt,
      failure.code,
      transcript,
    );
  } catch {
    console.error("Session failure finalization failed");
  }
  return failure;
}

export async function getSessionDetails(
  resources: SessionResources,
  input: GetSessionInput,
): Promise<SessionDetailsOutcome> {
  try {
    return {
      ok: true,
      value: await new SessionStore(
        resources.database,
        resources.artifacts,
      ).getDetails(input.sessionId, input.workspaceId, input.controllerUserId),
    };
  } catch (error) {
    return sessionFailure(error);
  }
}

export async function getSessionDiff(
  resources: SessionResources,
  input: GetSessionInput,
): Promise<SessionDiffOutcome> {
  try {
    return {
      ok: true,
      value: await new SessionStore(
        resources.database,
        resources.artifacts,
      ).readDiff(input.sessionId, input.workspaceId, input.controllerUserId),
    };
  } catch (error) {
    return sessionFailure(error);
  }
}

export async function discardSession(
  resources: SessionResources,
  input: DiscardSessionInput,
): Promise<DiscardSessionOutcome> {
  const sessions = new SessionStore(resources.database, resources.artifacts);
  try {
    const backup = await sessions.discard(
      input.sessionId,
      input.workspaceId,
      input.controllerUserId,
    );
    if (backup) {
      try {
        await deleteSandboxBackup(resources.backupBucket, backup);
      } catch {
        console.error("Discarded session cache cleanup failed");
      }
    }
    return { ok: true, value: undefined };
  } catch (error) {
    return sessionFailure(error);
  }
}

export async function publishSession(
  resources: SessionResources,
  input: PublishSessionInput,
): Promise<PublishSessionOutcome> {
  const sessions = new SessionStore(resources.database, resources.artifacts);
  let instance: Awaited<ReturnType<typeof createSandboxInstance>> | undefined;

  try {
    const context = await sessions.getPublication(
      input.sessionId,
      input.workspaceId,
      input.controllerUserId,
    );
    if (
      context.publishedRevision === context.revision
      && context.publishedCommitSha === context.commitSha
      && context.pullRequestNumber !== null
      && context.pullRequestUrl !== null
    ) {
      return {
        ok: true,
        value: {
          revision: context.revision,
          commitSha: context.commitSha,
          branch: context.publicationBranch,
          pullRequest: {
            number: context.pullRequestNumber,
            url: context.pullRequestUrl,
          },
        },
      };
    }

    const repository = await requireRepositoryAccess(
      resources.github,
      context.controllerUserId,
      context.githubRepositoryId,
      context.githubUserId,
      true,
    );
    const remote = await resources.github.getBranchHead(
      context.controllerUserId,
      context.githubRepositoryId,
      context.publicationBranch,
      context.githubUserId,
    );
    if (!remote.ok) throw new RepositoryAccessError(remote.code);
    const alreadyPushed = remote.value === context.commitSha;
    if (!alreadyPushed && remote.value !== context.publishedCommitSha) {
      return { ok: false, code: "publication_conflict" };
    }

    instance = await createSandboxInstance(resources.sandbox);
    const artifact = await sessions.restoreGit(context);
    requireCheckout(await restoreRepositoryCheckpoint(instance, {
      baseCommitSha: context.baseCommitSha,
      commitSha: context.commitSha,
      artifact,
    }));
    if (!alreadyPushed) {
      requirePublish(await publishRepositoryCheckpoint(instance, {
        sessionId: context.id,
        revision: context.revision,
        repositoryUrl: repository.cloneUrl,
        branch: context.publicationBranch,
        commitSha: context.commitSha,
        expectedRemoteCommitSha: context.publishedCommitSha,
        credential: credential(repository.accessToken),
      }));
    }

    const pullRequest = await resources.github.createPullRequest(
      context.controllerUserId,
      context.githubRepositoryId,
      {
        head: context.publicationBranch,
        base: context.baseRef,
        title: `AI Sloth session ${context.id}`,
        expectedGitHubUserId: context.githubUserId,
      },
    );
    if (!pullRequest.ok) throw new RepositoryAccessError(pullRequest.code);
    await sessions.savePublication(context, pullRequest.value);
    return {
      ok: true,
      value: {
        revision: context.revision,
        commitSha: context.commitSha,
        branch: context.publicationBranch,
        pullRequest: pullRequest.value,
      },
    };
  } catch (error) {
    return sessionFailure(error);
  } finally {
    if (instance) await destroySandboxInstance(instance);
  }
}

function accepted(attempt: SessionAttempt): SessionAccepted {
  return { sessionId: attempt.id, turnId: attempt.turnId, status: "running" };
}

async function requireRepositoryAccess(
  github: GitHubOperations,
  userId: string,
  repositoryId: string,
  expectedGitHubUserId: string | undefined,
  requirePush: boolean,
): Promise<GitHubRepositoryAccess> {
  const outcome = await github.getRepositoryAccess(
    userId,
    repositoryId,
    expectedGitHubUserId,
  );
  if (!outcome.ok) throw new RepositoryAccessError(outcome.code);
  if (requirePush && !outcome.value.canPush) {
    throw new RepositoryAccessError("access_denied");
  }
  return outcome.value;
}

function requireCheckout(result: RepositoryCheckoutResult): void {
  if (result.ok) return;
  throw new SessionRunError(
    result.timedOut ? "checkout_timeout" : "checkout_failed",
    result.details,
  );
}

function requirePublish(result: RepositoryPublishResult): void {
  if (result.ok) return;
  throw new SessionRunError(
    result.timedOut ? "checkpoint_timeout" : "checkpoint_failed",
    result.details,
  );
}

class RepositoryAccessError extends Error {
  constructor(readonly code: string) {
    super(code);
  }
}

function credential(accessToken: string) {
  return { username: "x-access-token", password: accessToken };
}

export function sessionFailure(error: unknown): SessionFailure {
  if (error instanceof SessionStoreError) {
    switch (error.code) {
      case "not_found":
      case "not_controller":
      case "revision_limit":
      case "turn_limit":
      case "session_limit":
      case "snapshot_too_large":
      case "transcript_too_large":
      case "diff_not_available":
      case "conflict":
        return { ok: false, code: error.code };
      case "artifact_too_large":
        return { ok: false, code: "checkpoint_too_large" };
      case "snapshot_missing":
      case "artifact_missing":
      case "stored_snapshot_too_large":
      case "stored_artifact_too_large":
      case "stored_transcript_invalid":
      case "stored_diff_invalid":
        return { ok: false, code: "internal_error" };
    }
  }
  if (error instanceof EventLogError) {
    return { ok: false, code: error.code };
  }
  if (error instanceof SessionRunError) {
    return { ok: false, code: error.code };
  }
  if (error instanceof ProjectContextError) {
    return {
      ok: false,
      code: error.code === "not_found"
        ? "project_not_found"
        : "project_instructions_too_large",
    };
  }
  if (error instanceof RepositoryAccessError) {
    switch (error.code) {
      case "not_connected":
        return { ok: false, code: "github_not_connected" };
      case "not_found":
        return { ok: false, code: "repository_not_found" };
      case "access_denied":
        return { ok: false, code: "repository_access_denied" };
      case "temporarily_unavailable":
        return { ok: false, code: "github_unavailable" };
      case "conflict":
        return { ok: false, code: "publication_conflict" };
      default:
        return { ok: false, code: "internal_error" };
    }
  }

  console.error("Session operation failed");
  return { ok: false, code: "internal_error" };
}
