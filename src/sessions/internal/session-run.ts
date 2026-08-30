import {
  checkoutRepository,
  createRepositoryCheckpoint,
  makeRepositoryWritableByAgent,
  restoreRepositoryCheckpoint,
  verifyRepositoryCommit,
  type RepositoryCheckoutResult,
  type RepositoryCheckpointResult,
} from "@ai-sloth/git";
import type { GitHubRepositoryAccess } from "@ai-sloth/github";
import { readPiSession, runPi } from "@ai-sloth/pi";
import {
  prepareProject,
  PROJECT_SETUP_VERSION,
  ProjectSetupError,
} from "@ai-sloth/project-setup";
import {
  createSandboxInstance,
  createSandboxBackup,
  deleteSandboxBackup,
  destroySandboxInstance,
  restoreSandboxBackup,
  stopAgentProcesses,
  type SandboxInstance,
  type SandboxBackup,
} from "@ai-sloth/sandbox";
import type { SessionResources } from "./contract";
import type { TurnEventLog } from "./event-log";
import {
  SessionStore,
  type ProjectCacheKey,
  type SessionAttempt,
} from "./session-store";

const HOT_BACKUP_TTL_SECONDS = 24 * 60 * 60;
const PROJECT_BACKUP_TTL_SECONDS = 7 * 24 * 60 * 60;

export async function runSession(
  resources: SessionResources,
  sessions: SessionStore,
  attempt: SessionAttempt,
  prompt: string,
  events: TurnEventLog,
  source?: GitHubRepositoryAccess,
): Promise<SessionRunResult> {
  const startedAt = performance.now();
  const phaseDurationsMs: Partial<Record<SessionRunPhase, number>> = {};
  let failedPhase: SessionRunPhase | undefined;
  let instance: SandboxInstance | undefined;
  let committed = false;
  let needsProjectBackup = false;

  const measure = async <T>(
    phase: SessionRunPhase,
    operation: () => Promise<T>,
  ): Promise<T> => {
    const phaseStartedAt = performance.now();
    try {
      return await operation();
    } catch (error) {
      failedPhase = phase;
      throw error;
    } finally {
      phaseDurationsMs[phase] = Math.round(performance.now() - phaseStartedAt);
    }
  };

  try {
    if (source) {
      const acquired = await measure(
        "sandboxAcquisition",
        () => acquireNewSessionSandbox(resources, sessions, attempt, source),
      );
      instance = acquired.instance;
      needsProjectBackup = acquired.needsProjectBackup;
    } else {
      instance = await measure(
        "sandboxAcquisition",
        () => restoreSessionSandbox(resources, sessions, attempt),
      );
    }

    const activeInstance = instance;
    await measure("projectPreparation", async () => {
      await makeRepositoryWritableByAgent(activeInstance);
      await prepareDependencies(activeInstance);
      await stopAgentProcesses(activeInstance);
    });
    if (needsProjectBackup) {
      await measure(
        "projectBackup",
        () => saveProjectBackup(resources, sessions, attempt, activeInstance),
      );
    }

    const previousRevision = attempt.previousRevision;
    const priorSession = previousRevision
      ? await measure(
        "sessionRestore",
        () => sessions.restorePi(previousRevision),
      )
      : undefined;
    const agent = requireAgent(await measure(
      "agent",
      () => runPi(activeInstance, {
        prompt,
        priorSession,
        projectInstructions: attempt.projectInstructions,
        onEvent: (event) => events.acceptPiEvent(event),
      }),
    ));
    const transcript = await events.finish();

    const checkpoint = await measure("checkpoint", async () => {
      await stopAgentProcesses(activeInstance);
      const piSession = await readPiSession(activeInstance);
      const repositoryCheckpoint = requireCheckpoint(
        await createRepositoryCheckpoint(activeInstance, {
          sessionId: attempt.id,
          revision: attempt.revision,
          baseCommitSha: attempt.baseCommitSha,
        }),
      );
      return { piSession, repositoryCheckpoint };
    });
    await measure(
      "commit",
      () => sessions.commit(
        attempt,
        checkpoint.repositoryCheckpoint.commitSha,
        checkpoint.repositoryCheckpoint.artifact,
        checkpoint.repositoryCheckpoint.diff,
        checkpoint.piSession,
        transcript,
      ),
    );
    committed = true;
    await measure(
      "hotBackup",
      () => saveHotBackup(resources, sessions, attempt, activeInstance),
    );

    return {
      commitSha: checkpoint.repositoryCheckpoint.commitSha,
      output: agent.stdout,
      truncated: agent.truncated,
    };
  } finally {
    const cleanupStartedAt = performance.now();
    if (instance) {
      try {
        await stopAgentProcesses(instance);
      } catch {
        // Sandbox destruction remains the final process cleanup boundary.
      }
      await destroySandboxInstance(instance);
    }
    phaseDurationsMs.cleanup = Math.round(performance.now() - cleanupStartedAt);
    console.info({
      event: "session.run",
      outcome: committed ? "success" : "failure",
      operation: source ? "start" : "continue",
      sessionId: attempt.id,
      turnId: attempt.turnId,
      revision: attempt.revision,
      serviceVersion: resources.serviceVersion,
      sandboxImageVersion: resources.imageVersion,
      durationMs: Math.round(performance.now() - startedAt),
      phaseDurationsMs,
      ...(failedPhase ? { failedPhase } : {}),
    });
  }
}

async function acquireNewSessionSandbox(
  resources: SessionResources,
  sessions: SessionStore,
  attempt: SessionAttempt,
  repository: GitHubRepositoryAccess,
): Promise<NewSessionSandbox> {
  const key = projectCacheKey(resources, attempt);
  const cached = await sessions.takeProjectBackup(key);
  await deleteBackups(resources, cached.stale);
  if (cached.backup) {
    const instance = await tryCachedSandbox(
      resources,
      cached.backup,
      async (instance) => {
        requireCheckout(
          await verifyRepositoryCommit(instance, attempt.baseCommitSha),
        );
      },
    );
    if (instance) return { instance, needsProjectBackup: false };
  }

  const instance = await createSandboxInstance(resources.sandbox);
  try {
    requireCheckout(await checkoutRepository(instance, {
      repositoryUrl: repository.cloneUrl,
      commitSha: attempt.baseCommitSha,
      credential: credential(repository.accessToken),
    }));
    return { instance, needsProjectBackup: true };
  } catch (error) {
    await destroySandboxInstance(instance);
    throw error;
  }
}

async function restoreSessionSandbox(
  resources: SessionResources,
  sessions: SessionStore,
  attempt: SessionAttempt,
): Promise<SandboxInstance> {
  const previous = attempt.previousRevision;
  if (!previous) throw new Error("Continued session has no previous revision");

  const hot = await sessions.takeHotBackup(attempt.id, previous.revision);
  const project = await sessions.takeProjectBackup(
    projectCacheKey(resources, attempt),
  );
  await deleteBackups(resources, [...hot.stale, ...project.stale]);

  const candidates = uniqueBackups([hot.backup, project.backup]);
  for (const backup of candidates) {
    const instance = await tryCachedSandbox(resources, backup, async (instance) => {
      const artifact = await sessions.restoreGit(previous);
      requireCheckout(await restoreRepositoryCheckpoint(instance, {
        baseCommitSha: attempt.baseCommitSha,
        commitSha: previous.commitSha,
        artifact,
      }));
    });
    if (instance) return instance;
  }

  const instance = await createSandboxInstance(resources.sandbox);
  try {
    const artifact = await sessions.restoreGit(previous);
    requireCheckout(await restoreRepositoryCheckpoint(instance, {
      baseCommitSha: attempt.baseCommitSha,
      commitSha: previous.commitSha,
      artifact,
    }));
    return instance;
  } catch (error) {
    await destroySandboxInstance(instance);
    throw error;
  }
}

async function tryCachedSandbox(
  resources: SessionResources,
  backup: SandboxBackup,
  validate: (instance: SandboxInstance) => Promise<void>,
): Promise<SandboxInstance | undefined> {
  let instance: SandboxInstance | undefined;
  try {
    // A failed restore never shares a sandbox with the slower reconstruction
    // path because createSandboxInstance destroys failures before returning.
    instance = await restoreSandboxBackup(resources.sandbox, backup);
    await validate(instance);
    return instance;
  } catch {
    if (instance) await destroySandboxInstance(instance);
    return undefined;
  }
}

async function prepareDependencies(instance: SandboxInstance): Promise<void> {
  try {
    await prepareProject(instance);
  } catch (error) {
    if (error instanceof ProjectSetupError) {
      throw new SessionRunError(error.code, error.details);
    }
    throw error;
  }
}

async function saveProjectBackup(
  resources: SessionResources,
  sessions: SessionStore,
  attempt: SessionAttempt,
  instance: SandboxInstance,
): Promise<void> {
  try {
    const backup = await createSandboxBackup(instance, {
      name: `project-${attempt.githubRepositoryId}-${attempt.baseCommitSha}`,
      ttlSeconds: PROJECT_BACKUP_TTL_SECONDS,
      local: resources.localBackups,
    });
    try {
      const stale = await sessions.saveProjectBackup(
        projectCacheKey(resources, attempt),
        backup,
        Date.now() + PROJECT_BACKUP_TTL_SECONDS * 1000,
      );
      await deleteBackups(resources, stale);
    } catch (error) {
      await deleteBackups(resources, [backup]);
      throw error;
    }
  } catch {
    console.error("Project sandbox backup failed");
  }
}

async function saveHotBackup(
  resources: SessionResources,
  sessions: SessionStore,
  attempt: SessionAttempt,
  instance: SandboxInstance,
): Promise<void> {
  try {
    const backup = await createSandboxBackup(instance, {
      name: `session-${attempt.id}-${attempt.revision}`,
      ttlSeconds: HOT_BACKUP_TTL_SECONDS,
      local: resources.localBackups,
    });
    try {
      const stale = await sessions.saveHotBackup(
        attempt.id,
        attempt.revision,
        backup,
        Date.now() + HOT_BACKUP_TTL_SECONDS * 1000,
      );
      await deleteBackups(resources, stale);
    } catch (error) {
      await deleteBackups(resources, [backup]);
      throw error;
    }
  } catch {
    console.error("Hot sandbox backup failed");
  }
}

async function deleteBackups(
  resources: SessionResources,
  backups: SandboxBackup[],
): Promise<void> {
  for (const backup of uniqueBackups(backups)) {
    try {
      await deleteSandboxBackup(resources.backupBucket, backup);
    } catch {
      console.error("Sandbox backup cleanup failed");
    }
  }
}

function projectCacheKey(
  resources: SessionResources,
  attempt: SessionAttempt,
): ProjectCacheKey {
  return {
    controllerUserId: attempt.controllerUserId,
    githubRepositoryId: attempt.githubRepositoryId,
    baseCommitSha: attempt.baseCommitSha,
    imageVersion: `${resources.imageVersion}:${PROJECT_SETUP_VERSION}`,
  };
}

function uniqueBackups(
  backups: Array<SandboxBackup | undefined>,
): SandboxBackup[] {
  const unique = new Map<string, SandboxBackup>();
  for (const backup of backups) {
    if (backup) unique.set(backup.id, backup);
  }
  return [...unique.values()];
}

export class SessionRunError extends Error {
  constructor(
    readonly code: SessionRunErrorCode,
    readonly details?: string,
  ) {
    super(code);
  }
}

function requireCheckout(
  result: RepositoryCheckoutResult,
): Extract<RepositoryCheckoutResult, { ok: true }> {
  if (result.ok) return result;
  throw new SessionRunError(
    result.timedOut ? "checkout_timeout" : "checkout_failed",
    result.details,
  );
}

function requireCheckpoint(
  result: RepositoryCheckpointResult,
): Extract<RepositoryCheckpointResult, { ok: true }> {
  if (result.ok) return result;
  throw new SessionRunError(
    result.timedOut
      ? "checkpoint_timeout"
      : result.tooLarge
        ? "checkpoint_too_large"
        : "checkpoint_failed",
    result.details,
  );
}

function requireAgent<T extends AgentResult>(result: T): T {
  if (result.timedOut) throw new SessionRunError("agent_timeout");
  if (result.exitCode !== 0) {
    throw new SessionRunError("agent_failed", result.stderr);
  }
  return result;
}

function credential(accessToken: string) {
  return { username: "x-access-token", password: accessToken };
}

type NewSessionSandbox = {
  instance: SandboxInstance;
  needsProjectBackup: boolean;
};

type SessionRunPhase =
  | "sandboxAcquisition"
  | "projectPreparation"
  | "projectBackup"
  | "sessionRestore"
  | "agent"
  | "checkpoint"
  | "commit"
  | "hotBackup"
  | "cleanup";

type AgentResult = {
  timedOut: boolean;
  exitCode: number;
  stderr: string;
};

type SessionRunResult = {
  commitSha: string;
  output: string;
  truncated: boolean;
};

type SessionRunErrorCode =
  | "checkout_timeout"
  | "checkout_failed"
  | "checkpoint_timeout"
  | "checkpoint_failed"
  | "checkpoint_too_large"
  | "setup_timeout"
  | "setup_failed"
  | "agent_timeout"
  | "agent_failed";
