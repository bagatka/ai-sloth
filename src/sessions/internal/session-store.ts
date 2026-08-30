import type { RepositoryArtifact } from "@ai-sloth/git";
import type { PiSessionSnapshot } from "@ai-sloth/pi";
import type { SandboxBackup } from "@ai-sloth/sandbox";
import { resolveProjectInstructions } from "./catalog";
import type {
  SessionAccepted,
  SessionDetails,
  SessionDiff,
  SessionTurnStatus,
} from "./contract";
import type { TurnTranscript } from "./event-log";

const MAX_PI_BYTES = 16 * 1024 * 1024;
const MAX_GIT_BYTES = 64 * 1024 * 1024;
const MAX_DIFF_BYTES = 16 * 1024 * 1024;
const MAX_SESSION_REVISIONS = 100;
const MAX_SESSION_TURNS = 100;
const MAX_CONTROLLER_SESSIONS = 20;
const MAX_TRANSCRIPT_BYTES = 8 * 1024 * 1024;
const MAX_PROJECT_BACKUPS = 3;

export class SessionStore {
  constructor(
    private readonly database: D1Database,
    private readonly artifacts: R2Bucket,
  ) {}

  start(input: {
    sessionId: string;
    turnId: string;
    idempotencyKey: string;
    workspaceId: string;
    controllerUserId: string;
    name: string;
    projectId: string | null;
    projectInstructions: string;
    githubRepositoryId: string;
    githubUserId: string;
    repositoryUrl: string;
    baseRef: string;
    baseCommitSha: string;
  }): SessionAttempt {
    return attempt({
      ...input,
      publicationBranch: `ai-sloth/${input.sessionId}`,
      revision: 1,
      turnOrdinal: 1,
      expectedCommitSha: "",
    });
  }

  async resume(
    sessionId: string,
    turnId: string,
    idempotencyKey: string,
    workspaceId: string,
    controllerUserId: string,
  ): Promise<SessionAttempt> {
    const stored = await this.database.prepare(LATEST_SESSION_SQL).bind(
      sessionId,
      workspaceId,
    ).first<StoredSession>();
    if (!stored) throw new SessionStoreError("not_found");
    if (stored.controllerUserId !== controllerUserId) {
      throw new SessionStoreError("not_controller");
    }
    if (stored.revision !== null && stored.revision >= MAX_SESSION_REVISIONS) {
      throw new SessionStoreError("revision_limit");
    }
    const turn = await this.database.prepare(LATEST_TURN_ORDINAL_SQL).bind(
      sessionId,
    ).first<{ ordinal: number }>();
    const turnOrdinal = (turn?.ordinal ?? 0) + 1;
    if (turnOrdinal > MAX_SESSION_TURNS) {
      throw new SessionStoreError("turn_limit");
    }
    const projectInstructions = await resolveProjectInstructions(this.database, {
      workspaceId,
      githubRepositoryId: stored.githubRepositoryId,
      projectId: stored.projectId,
    });
    return attempt({
      sessionId: stored.id,
      turnId,
      turnOrdinal,
      idempotencyKey,
      workspaceId,
      controllerUserId: stored.controllerUserId,
      name: stored.name,
      projectId: stored.projectId,
      projectInstructions,
      githubRepositoryId: stored.githubRepositoryId,
      githubUserId: stored.githubUserId,
      repositoryUrl: stored.repositoryUrl,
      baseRef: stored.baseRef,
      baseCommitSha: stored.baseCommitSha,
      publicationBranch: stored.publicationBranch,
      revision: (stored.revision ?? 0) + 1,
      expectedCommitSha: stored.commitSha ?? "",
      ...(stored.revision !== null
        && stored.commitSha !== null
        && stored.gitObjectKey !== null
        && stored.gitSize !== null
        && stored.piObjectKey !== null
        && stored.piSize !== null
        ? {
          previousRevision: {
            revision: stored.revision,
            commitSha: stored.commitSha,
            gitObjectKey: stored.gitObjectKey,
            gitSize: stored.gitSize,
            piObjectKey: stored.piObjectKey,
            piSize: stored.piSize,
          },
        }
        : {}),
    });
  }

  async reserve(attempt: SessionAttempt): Promise<void> {
    if (attempt.revision === 1) {
      await this.cleanupAbandonedAttempts();
      const count = await this.database.prepare(COUNT_CONTROLLER_SESSIONS_SQL)
        .bind(attempt.controllerUserId)
        .first<{ count: number }>();
      if ((count?.count ?? 0) >= MAX_CONTROLLER_SESSIONS) {
        throw new SessionStoreError("session_limit");
      }
    }

    const attemptWrite = this.database.prepare(INSERT_ATTEMPT_SQL).bind(
      attempt.id,
      attempt.workspaceId,
      attempt.controllerUserId,
      attempt.name,
      attempt.projectId,
      attempt.projectInstructions,
      attempt.githubRepositoryId,
      attempt.githubUserId,
      attempt.repositoryUrl,
      attempt.baseRef,
      attempt.baseCommitSha,
      attempt.publicationBranch,
      attempt.revision,
      attempt.expectedCommitSha,
      attempt.gitObjectKey,
      attempt.piObjectKey,
      Date.now(),
      attempt.turnId,
      attempt.transcriptObjectKey,
    );
    const turnWrite = this.database.prepare(INSERT_TURN_SQL).bind(
      attempt.turnId,
      attempt.id,
      attempt.turnOrdinal,
      attempt.idempotencyKey,
      attempt.transcriptObjectKey,
    );

    try {
      const writes = attempt.revision === 1
        ? await this.database.batch([
          this.database.prepare(INSERT_PENDING_SESSION_SQL).bind(
            attempt.id,
            attempt.workspaceId,
            attempt.controllerUserId,
            attempt.name,
            attempt.projectId,
            attempt.githubRepositoryId,
            attempt.githubUserId,
            attempt.repositoryUrl,
            attempt.baseRef,
            attempt.baseCommitSha,
            attempt.publicationBranch,
          ),
          attemptWrite,
          turnWrite,
        ])
        : await this.database.batch([attemptWrite, turnWrite]);
      if (writes.some((write) => !write.success)) {
        throw new Error("Could not reserve session turn");
      }
    } catch (error) {
      if (error instanceof Error && error.message.includes("UNIQUE constraint")) {
        throw new SessionStoreError("conflict");
      }
      throw error;
    }
  }

  async findAcceptedTurn(
    idempotencyKey: string,
    workspaceId: string,
    controllerUserId: string,
  ): Promise<SessionAccepted | null> {
    const stored = await this.database.prepare(FIND_IDEMPOTENT_TURN_SQL).bind(
      idempotencyKey,
      workspaceId,
      controllerUserId,
    ).first<{ sessionId: string; turnId: string; status: SessionTurnStatus }>();
    return stored
      ? {
        sessionId: stored.sessionId,
        turnId: stored.turnId,
        status: stored.status,
      }
      : null;
  }

  async getDetails(
    sessionId: string,
    workspaceId: string,
    controllerUserId: string,
  ): Promise<SessionDetails> {
    const session = await this.database.prepare(SESSION_DETAILS_SQL).bind(
      sessionId,
      workspaceId,
    ).first<StoredSessionDetails>();
    if (!session) throw new SessionStoreError("not_found");
    if (session.controllerUserId !== controllerUserId) {
      throw new SessionStoreError("not_controller");
    }
    const turns = await this.database.prepare(SESSION_TURNS_SQL).bind(sessionId)
      .all<StoredSessionTurn>();
    if (!turns.success) throw new Error("Could not load session turns");
    return {
      id: session.id,
      name: session.name,
      workspaceId: session.workspaceId,
      githubRepositoryId: session.githubRepositoryId,
      projectId: session.projectId,
      status: sessionStatus(session.currentRevision, turns.results.at(-1)?.status),
      revision: session.currentRevision,
      publication: session.publishedRevision !== null
          && session.publishedCommitSha !== null
          && session.pullRequestNumber !== null
          && session.pullRequestUrl !== null
        ? {
          revision: session.publishedRevision,
          commitSha: session.publishedCommitSha,
          branch: session.publicationBranch,
          pullRequest: {
            number: session.pullRequestNumber,
            url: session.pullRequestUrl,
          },
        }
        : null,
      createdAt: session.createdAt,
      updatedAt: session.updatedAt,
      turns: turns.results.map((turn) => ({
        id: turn.id,
        ordinal: turn.ordinal,
        status: turn.status,
        failureCode: turn.failureCode,
        resultRevision: turn.resultRevision,
        lastEventSequence: turn.lastEventSequence ?? 0,
        createdAt: turn.createdAt,
        completedAt: turn.completedAt,
      })),
    };
  }

  async readDiff(
    sessionId: string,
    workspaceId: string,
    controllerUserId: string,
  ): Promise<SessionDiff> {
    const stored = await this.database.prepare(SESSION_DIFF_SQL).bind(
      sessionId,
      workspaceId,
    ).first<{
      controllerUserId: string;
      revision: number | null;
      gitObjectKey: string | null;
    }>();
    if (!stored) throw new SessionStoreError("not_found");
    if (stored.controllerUserId !== controllerUserId) {
      throw new SessionStoreError("not_controller");
    }
    if (stored.revision === null || stored.gitObjectKey === null) {
      throw new SessionStoreError("diff_not_available");
    }
    const object = await this.artifacts.get(diffObjectKey(stored.gitObjectKey));
    if (!object) throw new SessionStoreError("diff_not_available");
    if (object.size > MAX_DIFF_BYTES) {
      throw new SessionStoreError("stored_diff_invalid");
    }
    return {
      revision: stored.revision,
      size: object.size,
      content: object.body,
    };
  }

  async getTurn(
    sessionId: string,
    turnId: string,
    workspaceId: string,
    controllerUserId: string,
  ): Promise<StoredSessionTurn> {
    const stored = await this.database.prepare(GET_TURN_SQL).bind(
      turnId,
      sessionId,
      workspaceId,
    ).first<StoredSessionTurn & { controllerUserId: string }>();
    if (!stored) throw new SessionStoreError("not_found");
    if (stored.controllerUserId !== controllerUserId) {
      throw new SessionStoreError("not_controller");
    }
    return stored;
  }

  async readTranscript(turn: StoredSessionTurn): Promise<R2ObjectBody | null> {
    if (turn.transcriptSize === null) return null;
    const object = await this.artifacts.get(turn.transcriptObjectKey);
    if (!object || object.size !== turn.transcriptSize
      || object.size > MAX_TRANSCRIPT_BYTES) {
      throw new SessionStoreError("stored_transcript_invalid");
    }
    return object;
  }

  async restoreGit(
    revision: StoredRevision,
  ): Promise<RepositoryArtifact> {
    const object = await this.artifacts.get(revision.gitObjectKey);
    if (!object) throw new SessionStoreError("artifact_missing");
    if (object.size !== revision.gitSize || object.size > MAX_GIT_BYTES) {
      throw new SessionStoreError("stored_artifact_too_large");
    }
    return { content: object.body, size: object.size };
  }

  async restorePi(
    revision: StoredRevision,
  ): Promise<ReadableStream<Uint8Array>> {
    const object = await this.artifacts.get(revision.piObjectKey);
    if (!object) throw new SessionStoreError("snapshot_missing");
    if (object.size !== revision.piSize || object.size > MAX_PI_BYTES) {
      throw new SessionStoreError("stored_snapshot_too_large");
    }
    return object.body;
  }

  async commit(
    attempt: SessionAttempt,
    commitSha: string,
    git: RepositoryArtifact,
    diff: RepositoryArtifact,
    pi: PiSessionSnapshot,
    transcript: TurnTranscript,
  ): Promise<void> {
    if (git.size > MAX_GIT_BYTES) throw new SessionStoreError("artifact_too_large");
    if (diff.size > MAX_DIFF_BYTES) throw new SessionStoreError("artifact_too_large");
    if (pi.size > MAX_PI_BYTES) throw new SessionStoreError("snapshot_too_large");
    if (transcript.size > MAX_TRANSCRIPT_BYTES) {
      throw new SessionStoreError("transcript_too_large");
    }

    await this.markFinalizing(attempt.turnId, null);
    await this.putTranscript(attempt, transcript);
    await this.artifacts.put(
      attempt.diffObjectKey,
      diff.content.pipeThrough(new FixedLengthStream(diff.size)),
      { httpMetadata: { contentType: "text/x-diff; charset=utf-8" } },
    );
    await this.artifacts.put(
      attempt.gitObjectKey,
      git.content.pipeThrough(new FixedLengthStream(git.size)),
      { httpMetadata: { contentType: "application/x-git-bundle" } },
    );
    try {
      await this.artifacts.put(
        attempt.piObjectKey,
        pi.content.pipeThrough(new FixedLengthStream(pi.size)),
        { httpMetadata: { contentType: "application/x-ndjson" } },
      );
    } catch (error) {
      await this.deleteArtifacts(attempt, true);
      throw error;
    }

    try {
      await this.finishCommit(
        attempt,
        commitSha,
        git.size,
        pi.size,
        transcript,
      );
    } catch (error) {
      if (await this.isCommitted(attempt)) return;
      throw error;
    }
  }

  async fail(
    attempt: SessionAttempt,
    code: string,
    transcript?: TurnTranscript,
  ): Promise<void> {
    await this.markFinalizing(attempt.turnId, code);
    if (transcript) await this.putTranscript(attempt, transcript);
    const writes = await this.database.batch([
      this.database.prepare(FAIL_TURN_SQL).bind(
        code,
        transcript?.size ?? null,
        transcript?.lastSequence ?? null,
        attempt.turnId,
      ),
      this.database.prepare(DELETE_ATTEMPT_SQL).bind(attempt.id),
    ]);
    if (writes.some((write) => !write.success) || !changed(writes[0])) {
      throw new Error("Could not fail session turn");
    }
    await this.deleteObjectKeys([
      attempt.gitObjectKey,
      attempt.diffObjectKey,
      attempt.piObjectKey,
    ]);
  }

  async interrupt(
    sessionId: string,
    turn: StoredSessionTurn,
    transcript: TurnTranscript,
  ): Promise<void> {
    await this.markFinalizing(turn.id, "interrupted");
    await this.artifacts.put(
      turn.transcriptObjectKey,
      transcript.content,
      { httpMetadata: { contentType: "application/x-ndjson" } },
    );
    const pending = await this.database.prepare(FIND_ATTEMPT_SQL).bind(sessionId)
      .first<StoredAttempt>();
    const writes = await this.database.batch([
      this.database.prepare(FAIL_TURN_SQL).bind(
        "interrupted",
        transcript.size,
        transcript.lastSequence,
        turn.id,
      ),
      this.database.prepare(DELETE_ATTEMPT_SQL).bind(sessionId),
    ]);
    if (writes.some((write) => !write.success) || !changed(writes[0])) {
      throw new Error("Could not interrupt session turn");
    }
    if (pending) {
      await this.deleteObjectKeys([
        pending.gitObjectKey,
        diffObjectKey(pending.gitObjectKey),
        pending.piObjectKey,
      ]);
    }
  }

  async discard(
    sessionId: string,
    workspaceId: string,
    controllerUserId: string,
  ): Promise<SandboxBackup | undefined> {
    const stored = await this.database.prepare(DISCARD_SESSION_SQL).bind(
      sessionId,
      workspaceId,
    ).first<{ controllerUserId: string }>();
    if (!stored) throw new SessionStoreError("not_found");
    if (stored.controllerUserId !== controllerUserId) {
      throw new SessionStoreError("not_controller");
    }
    const marked = await this.database.prepare(MARK_SESSION_DELETING_SQL)
      .bind(sessionId).run();
    if (!marked.success) throw new Error("Could not mark session for deletion");

    const revisions = await this.database.prepare(SESSION_ARTIFACT_KEYS_SQL)
      .bind(sessionId, sessionId, sessionId, sessionId, sessionId)
      .all<{ objectKey: string }>();
    const hot = await this.database.prepare(HOT_BACKUP_SQL).bind(sessionId)
      .first<StoredBackup>();
    await this.artifacts.delete(revisions.results.flatMap(({ objectKey }) =>
      objectKey.endsWith(".bundle")
        ? [objectKey, diffObjectKey(objectKey)]
        : [objectKey]
    ));

    const writes = await this.database.batch([
      this.database.prepare(DELETE_HOT_BACKUP_SQL).bind(sessionId),
      this.database.prepare(DELETE_ATTEMPT_SQL).bind(sessionId),
      this.database.prepare(DELETE_SESSION_TURNS_SQL).bind(sessionId),
      this.database.prepare(DELETE_SESSION_REVISIONS_SQL).bind(sessionId),
      this.database.prepare(DELETE_SESSION_SQL).bind(sessionId),
    ]);
    if (writes.some((write) => !write.success)) {
      throw new Error("Could not finish session deletion");
    }
    return hot ? sandboxBackup(hot) : undefined;
  }

  async getPublication(
    sessionId: string,
    workspaceId: string,
    controllerUserId: string,
  ): Promise<PublicationContext> {
    const stored = await this.database.prepare(PUBLICATION_SQL).bind(
      sessionId,
      workspaceId,
    ).first<PublicationContext>();
    if (!stored) throw new SessionStoreError("not_found");
    if (stored.controllerUserId !== controllerUserId) {
      throw new SessionStoreError("not_controller");
    }
    return stored;
  }

  async savePublication(
    context: PublicationContext,
    pullRequest: { number: number; url: string },
  ): Promise<void> {
    const write = await this.database.prepare(SAVE_PUBLICATION_SQL).bind(
      context.revision,
      context.commitSha,
      pullRequest.number,
      pullRequest.url,
      context.id,
      context.revision,
      context.commitSha,
    ).run();
    if (!write.success || !changed(write)) {
      throw new SessionStoreError("conflict");
    }
  }

  async takeHotBackup(
    sessionId: string,
    revision: number,
  ): Promise<CacheLookup> {
    const stored = await this.database.prepare(HOT_BACKUP_SQL).bind(sessionId)
      .first<StoredBackup>();
    if (!stored) return { stale: [] };
    const backup = sandboxBackup(stored);
    if (stored.revision === revision && stored.expiresAt > Date.now()) {
      return { backup, stale: [] };
    }
    await this.database.prepare(DELETE_HOT_BACKUP_SQL).bind(sessionId).run();
    return { stale: [backup] };
  }

  async saveHotBackup(
    sessionId: string,
    revision: number,
    backup: SandboxBackup,
    expiresAt: number,
  ): Promise<SandboxBackup[]> {
    const previous = await this.database.prepare(HOT_BACKUP_SQL).bind(sessionId)
      .first<StoredBackup>();
    const write = await this.database.prepare(SAVE_HOT_BACKUP_SQL).bind(
      sessionId,
      revision,
      backup.id,
      backup.local ? 1 : 0,
      expiresAt,
    ).run();
    if (!write.success) throw new Error("Could not save hot sandbox backup");
    return previous ? [sandboxBackup(previous)] : [];
  }

  async takeProjectBackup(input: ProjectCacheKey): Promise<CacheLookup> {
    const stored = await this.database.prepare(PROJECT_BACKUP_SQL).bind(
      input.controllerUserId,
      input.githubRepositoryId,
      input.baseCommitSha,
      input.imageVersion,
    ).first<StoredBackup>();
    if (!stored) return { stale: [] };
    const backup = sandboxBackup(stored);
    if (stored.expiresAt > Date.now()) {
      await this.database.prepare(TOUCH_PROJECT_BACKUP_SQL).bind(
        Date.now(),
        input.controllerUserId,
        input.githubRepositoryId,
        input.baseCommitSha,
        input.imageVersion,
      ).run();
      return { backup, stale: [] };
    }
    await this.deleteProjectBackup(input);
    return { stale: [backup] };
  }

  async saveProjectBackup(
    input: ProjectCacheKey,
    backup: SandboxBackup,
    expiresAt: number,
  ): Promise<SandboxBackup[]> {
    const previous = await this.database.prepare(PROJECT_BACKUP_SQL).bind(
      input.controllerUserId,
      input.githubRepositoryId,
      input.baseCommitSha,
      input.imageVersion,
    ).first<StoredBackup>();
    const now = Date.now();
    const write = await this.database.prepare(SAVE_PROJECT_BACKUP_SQL).bind(
      input.controllerUserId,
      input.githubRepositoryId,
      input.baseCommitSha,
      input.imageVersion,
      backup.id,
      backup.local ? 1 : 0,
      expiresAt,
      now,
    ).run();
    if (!write.success) throw new Error("Could not save project sandbox backup");

    const excess = await this.database.prepare(EXCESS_PROJECT_BACKUPS_SQL).bind(
      input.controllerUserId,
      input.githubRepositoryId,
      input.imageVersion,
      MAX_PROJECT_BACKUPS,
    ).all<StoredProjectBackup>();
    const stale = excess.results.map(sandboxBackup);
    if (excess.results.length > 0) {
      await this.database.batch(excess.results.map((item) =>
        this.database.prepare(DELETE_PROJECT_BACKUP_SQL).bind(
          item.controllerUserId,
          item.githubRepositoryId,
          item.baseCommitSha,
          item.imageVersion,
        )
      ));
    }
    if (previous) stale.push(sandboxBackup(previous));
    return uniqueBackups(stale, backup.id);
  }

  private async isCommitted(attempt: SessionAttempt): Promise<boolean> {
    const stored = await this.database.prepare(COMMITTED_ATTEMPT_SQL).bind(
      attempt.id,
      attempt.revision,
    ).first<{ gitObjectKey: string; piObjectKey: string }>();
    return stored?.gitObjectKey === attempt.gitObjectKey
      && stored.piObjectKey === attempt.piObjectKey;
  }

  private async cleanupAbandonedAttempts(): Promise<void> {
    const expired = await this.database.prepare(EXPIRED_ATTEMPTS_SQL)
      .bind(Date.now() - 60 * 60 * 1000)
      .all<StoredAttempt>();
    if (expired.results.length === 0) return;
    await this.database.batch(expired.results.flatMap((item) => [
      ...(item.turnId
        ? [this.database.prepare(INTERRUPT_ABANDONED_TURN_SQL).bind(item.turnId)]
        : []),
      this.database.prepare(DELETE_ATTEMPT_SQL).bind(item.sessionId),
    ]));
    await this.deleteObjectKeys(expired.results.flatMap((item) => [
      item.gitObjectKey,
      diffObjectKey(item.gitObjectKey),
      item.piObjectKey,
    ]));
  }

  private async finishCommit(
    attempt: SessionAttempt,
    commitSha: string,
    gitSize: number,
    piSize: number,
    transcript: TurnTranscript,
  ): Promise<void> {
    const revisionWrite = attempt.revision === 1
      ? this.database.prepare(INSERT_FIRST_REVISION_SQL).bind(
        attempt.id,
        attempt.revision,
        commitSha,
        attempt.gitObjectKey,
        gitSize,
        attempt.piObjectKey,
        piSize,
        attempt.projectInstructions,
        attempt.id,
      )
      : this.database.prepare(INSERT_CONTINUED_REVISION_SQL).bind(
        attempt.id,
        attempt.revision,
        commitSha,
        attempt.gitObjectKey,
        gitSize,
        attempt.piObjectKey,
        piSize,
        attempt.projectInstructions,
        attempt.id,
        attempt.revision - 1,
        attempt.expectedCommitSha,
      );
    const sessionWrite = attempt.revision === 1
      ? this.database.prepare(UPDATE_FIRST_SESSION_REVISION_SQL).bind(
        attempt.revision,
        commitSha,
        attempt.id,
      )
      : this.database.prepare(UPDATE_SESSION_SQL).bind(
        attempt.revision,
        commitSha,
        attempt.id,
        attempt.revision - 1,
        attempt.expectedCommitSha,
      );
    const writes = await this.database.batch([
      revisionWrite,
      sessionWrite,
      this.database.prepare(COMPLETE_TURN_SQL).bind(
        attempt.revision,
        transcript.size,
        transcript.lastSequence,
        attempt.turnId,
      ),
      this.database.prepare(DELETE_ATTEMPT_SQL).bind(attempt.id),
    ]);
    if (writes.some((write) => !write.success)) {
      throw new Error("Could not commit session revision");
    }
    if (!changed(writes[0]) || !changed(writes[1]) || !changed(writes[2])) {
      throw new SessionStoreError("conflict");
    }
  }

  private async markFinalizing(
    turnId: string,
    failureCode: string | null,
  ): Promise<void> {
    const write = await this.database.prepare(MARK_TURN_FINALIZING_SQL).bind(
      failureCode,
      turnId,
    ).run();
    if (!write.success || !changed(write)) {
      throw new SessionStoreError("conflict");
    }
  }

  private putTranscript(
    attempt: SessionAttempt,
    transcript: TurnTranscript,
  ): Promise<R2Object> {
    return this.artifacts.put(
      attempt.transcriptObjectKey,
      transcript.content,
      { httpMetadata: { contentType: "application/x-ndjson" } },
    );
  }

  private deleteProjectBackup(input: ProjectCacheKey): Promise<D1Result> {
    return this.database.prepare(DELETE_PROJECT_BACKUP_SQL).bind(
      input.controllerUserId,
      input.githubRepositoryId,
      input.baseCommitSha,
      input.imageVersion,
    ).run();
  }

  private deleteArtifacts(
    attempt: SessionAttempt,
    includeTranscript = false,
  ): Promise<void> {
    return this.deleteObjectKeys([
      attempt.gitObjectKey,
      attempt.diffObjectKey,
      attempt.piObjectKey,
      ...(includeTranscript ? [attempt.transcriptObjectKey] : []),
    ]);
  }

  private async deleteObjectKeys(keys: string[]): Promise<void> {
    try {
      await this.artifacts.delete(keys);
    } catch {
      console.error("Could not delete abandoned session artifacts");
    }
  }
}

function diffObjectKey(gitObjectKey: string): string {
  return gitObjectKey.endsWith(".bundle")
    ? `${gitObjectKey.slice(0, -".bundle".length)}.diff`
    : `${gitObjectKey}.diff`;
}

export class SessionStoreError extends Error {
  constructor(readonly code: SessionStoreErrorCode) {
    super(code);
  }
}

function attempt(input: AttemptInput): SessionAttempt {
  const revision = input.revision.toString().padStart(8, "0");
  const nonce = crypto.randomUUID();
  const gitObjectKey =
    `sessions/${input.sessionId}/${revision}-${nonce}.bundle`;
  return {
    id: input.sessionId,
    turnId: input.turnId,
    turnOrdinal: input.turnOrdinal,
    idempotencyKey: input.idempotencyKey,
    workspaceId: input.workspaceId,
    controllerUserId: input.controllerUserId,
    name: input.name,
    projectId: input.projectId,
    projectInstructions: input.projectInstructions,
    githubRepositoryId: input.githubRepositoryId,
    githubUserId: input.githubUserId,
    repositoryUrl: input.repositoryUrl,
    baseRef: input.baseRef,
    baseCommitSha: input.baseCommitSha,
    publicationBranch: input.publicationBranch,
    revision: input.revision,
    expectedCommitSha: input.expectedCommitSha,
    gitObjectKey,
    piObjectKey: `sessions/${input.sessionId}/${revision}-${nonce}.jsonl`,
    diffObjectKey: diffObjectKey(gitObjectKey),
    transcriptObjectKey:
      `sessions/${input.sessionId}/turns/${input.turnId}.ndjson`,
    ...(input.previousRevision
      ? { previousRevision: input.previousRevision }
      : {}),
  };
}

function sessionStatus(
  revision: number | null,
  latestTurn: SessionTurnStatus | undefined,
): SessionDetails["status"] {
  if (latestTurn === "running") return "running";
  if (latestTurn === "finalizing") return "waiting";
  if (revision !== null) return "completed";
  return "failed";
}

function changed(write: D1Result): boolean {
  return (write.meta.changes ?? 0) > 0;
}

function sandboxBackup(stored: StoredBackup): SandboxBackup {
  return { id: stored.backupId, local: stored.local === 1 };
}

function uniqueBackups(
  backups: SandboxBackup[],
  currentId: string,
): SandboxBackup[] {
  const unique = new Map<string, SandboxBackup>();
  for (const backup of backups) {
    if (backup.id !== currentId) unique.set(backup.id, backup);
  }
  return [...unique.values()];
}

const COUNT_CONTROLLER_SESSIONS_SQL = `
  SELECT COUNT(*) AS count FROM durable_sessions WHERE controller_user_id = ?
`;

const LATEST_SESSION_SQL = `
  SELECT
    sessions.id,
    sessions.controller_user_id AS controllerUserId,
    sessions.name,
    sessions.project_id AS projectId,
    sessions.github_repository_id AS githubRepositoryId,
    sessions.github_user_id AS githubUserId,
    sessions.repository_url AS repositoryUrl,
    sessions.base_ref AS baseRef,
    sessions.base_commit_sha AS baseCommitSha,
    sessions.publication_branch AS publicationBranch,
    sessions.current_revision AS revision,
    sessions.current_commit_sha AS commitSha,
    revisions.git_object_key AS gitObjectKey,
    revisions.git_size AS gitSize,
    revisions.pi_object_key AS piObjectKey,
    revisions.pi_size AS piSize
  FROM durable_sessions AS sessions
  JOIN durable_session_revisions AS revisions
    ON revisions.session_id = sessions.id
    AND revisions.revision = sessions.current_revision
  WHERE sessions.id = ? AND sessions.workspace_id = ?
    AND sessions.deleting = 0
`;

const INSERT_ATTEMPT_SQL = `
  INSERT INTO durable_revision_attempts (
    session_id, workspace_id, controller_user_id, name, project_id,
    project_instructions, github_repository_id, github_user_id, repository_url,
    base_ref, base_commit_sha, publication_branch, revision,
    expected_commit_sha, git_object_key, pi_object_key, created_at,
    turn_id, transcript_object_key
  ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
`;

const INSERT_TURN_SQL = `
  INSERT INTO session_turns (
    id, session_id, ordinal, idempotency_key, status, transcript_object_key
  ) VALUES (?, ?, ?, ?, 'running', ?)
`;

const FIND_IDEMPOTENT_TURN_SQL = `
  SELECT
    sessions.id AS sessionId,
    turns.id AS turnId,
    turns.status
  FROM session_turns AS turns
  JOIN durable_sessions AS sessions ON sessions.id = turns.session_id
  WHERE turns.idempotency_key = ?
    AND sessions.workspace_id = ?
    AND sessions.controller_user_id = ?
    AND sessions.deleting = 0
`;

const LATEST_TURN_ORDINAL_SQL = `
  SELECT ordinal FROM session_turns
  WHERE session_id = ?
  ORDER BY ordinal DESC
  LIMIT 1
`;

const COMMITTED_ATTEMPT_SQL = `
  SELECT git_object_key AS gitObjectKey, pi_object_key AS piObjectKey
  FROM durable_session_revisions
  WHERE session_id = ? AND revision = ?
`;

const FIND_ATTEMPT_SQL = `
  SELECT
    session_id AS sessionId,
    workspace_id AS workspaceId,
    controller_user_id AS controllerUserId,
    git_object_key AS gitObjectKey,
    pi_object_key AS piObjectKey,
    turn_id AS turnId
  FROM durable_revision_attempts WHERE session_id = ?
`;

const EXPIRED_ATTEMPTS_SQL = `
  SELECT
    session_id AS sessionId,
    workspace_id AS workspaceId,
    controller_user_id AS controllerUserId,
    git_object_key AS gitObjectKey,
    pi_object_key AS piObjectKey,
    turn_id AS turnId
  FROM durable_revision_attempts
  WHERE created_at < ?
  LIMIT 20
`;

const INTERRUPT_ABANDONED_TURN_SQL = `
  UPDATE session_turns
  SET status = 'interrupted', failure_code = 'interrupted',
      completed_at = CURRENT_TIMESTAMP
  WHERE id = ? AND status IN ('running', 'finalizing')
`;

const DELETE_ATTEMPT_SQL = `
  DELETE FROM durable_revision_attempts WHERE session_id = ?
`;

const INSERT_PENDING_SESSION_SQL = `
  INSERT INTO durable_sessions (
    id, workspace_id, controller_user_id, name, project_id,
    github_repository_id, github_user_id, repository_url, base_ref,
    base_commit_sha, publication_branch, current_revision, current_commit_sha
  ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, NULL, NULL)
`;

const INSERT_FIRST_REVISION_SQL = `
  INSERT INTO durable_session_revisions (
    session_id, revision, commit_sha, git_object_key, git_size,
    pi_object_key, pi_size, project_instructions
  )
  SELECT ?, ?, ?, ?, ?, ?, ?, ?
  WHERE EXISTS (
    SELECT 1 FROM durable_sessions
    WHERE id = ? AND current_revision IS NULL AND current_commit_sha IS NULL
  )
`;

const INSERT_CONTINUED_REVISION_SQL = `
  INSERT INTO durable_session_revisions (
    session_id, revision, commit_sha, git_object_key, git_size,
    pi_object_key, pi_size, project_instructions
  )
  SELECT ?, ?, ?, ?, ?, ?, ?, ?
  WHERE EXISTS (
    SELECT 1 FROM durable_sessions
    WHERE id = ? AND current_revision = ? AND current_commit_sha = ?
  )
`;

const UPDATE_FIRST_SESSION_REVISION_SQL = `
  UPDATE durable_sessions
  SET current_revision = ?, current_commit_sha = ?, updated_at = CURRENT_TIMESTAMP
  WHERE id = ? AND current_revision IS NULL AND current_commit_sha IS NULL
`;

const UPDATE_SESSION_SQL = `
  UPDATE durable_sessions
  SET current_revision = ?, current_commit_sha = ?, updated_at = CURRENT_TIMESTAMP
  WHERE id = ? AND current_revision = ? AND current_commit_sha = ?
`;

const MARK_TURN_FINALIZING_SQL = `
  UPDATE session_turns
  SET status = 'finalizing', failure_code = ?
  WHERE id = ? AND status IN ('running', 'finalizing')
`;

const COMPLETE_TURN_SQL = `
  UPDATE session_turns
  SET status = 'succeeded', result_revision = ?, transcript_size = ?,
      last_event_sequence = ?, completed_at = CURRENT_TIMESTAMP
  WHERE id = ? AND status = 'finalizing'
`;

const FAIL_TURN_SQL = `
  UPDATE session_turns
  SET status = CASE WHEN ? = 'interrupted' THEN 'interrupted' ELSE 'failed' END,
      transcript_size = ?, last_event_sequence = ?,
      completed_at = CURRENT_TIMESTAMP
  WHERE id = ? AND status = 'finalizing'
`;

const DISCARD_SESSION_SQL = `
  SELECT controller_user_id AS controllerUserId
  FROM durable_sessions
  WHERE id = ? AND workspace_id = ?
`;
const MARK_SESSION_DELETING_SQL = `
  UPDATE durable_sessions SET deleting = 1, updated_at = CURRENT_TIMESTAMP
  WHERE id = ?
`;
const SESSION_ARTIFACT_KEYS_SQL = `
  SELECT git_object_key AS objectKey
  FROM durable_session_revisions WHERE session_id = ?
  UNION ALL
  SELECT pi_object_key AS objectKey
  FROM durable_session_revisions WHERE session_id = ?
  UNION ALL
  SELECT transcript_object_key AS objectKey
  FROM session_turns WHERE session_id = ? AND transcript_size IS NOT NULL
  UNION ALL
  SELECT git_object_key AS objectKey
  FROM durable_revision_attempts WHERE session_id = ?
  UNION ALL
  SELECT pi_object_key AS objectKey
  FROM durable_revision_attempts WHERE session_id = ?
`;
const DELETE_SESSION_TURNS_SQL = `DELETE FROM session_turns WHERE session_id = ?`;
const DELETE_SESSION_REVISIONS_SQL = `
  DELETE FROM durable_session_revisions WHERE session_id = ?
`;
const DELETE_SESSION_SQL = `DELETE FROM durable_sessions WHERE id = ?`;

const SESSION_DETAILS_SQL = `
  SELECT
    id,
    workspace_id AS workspaceId,
    controller_user_id AS controllerUserId,
    name,
    project_id AS projectId,
    github_repository_id AS githubRepositoryId,
    current_revision AS currentRevision,
    publication_branch AS publicationBranch,
    published_revision AS publishedRevision,
    published_commit_sha AS publishedCommitSha,
    pull_request_number AS pullRequestNumber,
    pull_request_url AS pullRequestUrl,
    created_at AS createdAt,
    updated_at AS updatedAt
  FROM durable_sessions
  WHERE id = ? AND workspace_id = ? AND deleting = 0
`;

const SESSION_TURNS_SQL = `
  SELECT
    id,
    ordinal,
    status,
    failure_code AS failureCode,
    result_revision AS resultRevision,
    transcript_object_key AS transcriptObjectKey,
    transcript_size AS transcriptSize,
    last_event_sequence AS lastEventSequence,
    created_at AS createdAt,
    completed_at AS completedAt
  FROM session_turns
  WHERE session_id = ?
  ORDER BY ordinal
`;

const SESSION_DIFF_SQL = `
  SELECT
    sessions.controller_user_id AS controllerUserId,
    sessions.current_revision AS revision,
    revisions.git_object_key AS gitObjectKey
  FROM durable_sessions AS sessions
  LEFT JOIN durable_session_revisions AS revisions
    ON revisions.session_id = sessions.id
    AND revisions.revision = sessions.current_revision
  WHERE sessions.id = ? AND sessions.workspace_id = ?
    AND sessions.deleting = 0
`;

const GET_TURN_SQL = `
  SELECT
    turns.id,
    turns.ordinal,
    turns.status,
    turns.failure_code AS failureCode,
    turns.result_revision AS resultRevision,
    turns.transcript_object_key AS transcriptObjectKey,
    turns.transcript_size AS transcriptSize,
    turns.last_event_sequence AS lastEventSequence,
    turns.created_at AS createdAt,
    turns.completed_at AS completedAt,
    sessions.controller_user_id AS controllerUserId
  FROM session_turns AS turns
  JOIN durable_sessions AS sessions ON sessions.id = turns.session_id
  WHERE turns.id = ? AND sessions.id = ? AND sessions.workspace_id = ?
    AND sessions.deleting = 0
`;

const PUBLICATION_SQL = `
  SELECT
    sessions.id,
    sessions.controller_user_id AS controllerUserId,
    sessions.github_repository_id AS githubRepositoryId,
    sessions.github_user_id AS githubUserId,
    sessions.repository_url AS repositoryUrl,
    sessions.base_ref AS baseRef,
    sessions.base_commit_sha AS baseCommitSha,
    sessions.publication_branch AS publicationBranch,
    sessions.current_revision AS revision,
    sessions.current_commit_sha AS commitSha,
    sessions.published_revision AS publishedRevision,
    sessions.published_commit_sha AS publishedCommitSha,
    sessions.pull_request_number AS pullRequestNumber,
    sessions.pull_request_url AS pullRequestUrl,
    revisions.git_object_key AS gitObjectKey,
    revisions.git_size AS gitSize,
    revisions.pi_object_key AS piObjectKey,
    revisions.pi_size AS piSize
  FROM durable_sessions AS sessions
  LEFT JOIN durable_session_revisions AS revisions
    ON revisions.session_id = sessions.id
    AND revisions.revision = sessions.current_revision
  WHERE sessions.id = ? AND sessions.workspace_id = ?
    AND sessions.deleting = 0
`;

const SAVE_PUBLICATION_SQL = `
  UPDATE durable_sessions
  SET published_revision = ?, published_commit_sha = ?,
      pull_request_number = ?, pull_request_url = ?,
      updated_at = CURRENT_TIMESTAMP
  WHERE id = ? AND current_revision = ? AND current_commit_sha = ?
`;

const HOT_BACKUP_SQL = `
  SELECT revision, backup_id AS backupId, local, expires_at AS expiresAt
  FROM session_hot_backups WHERE session_id = ?
`;
const DELETE_HOT_BACKUP_SQL = `DELETE FROM session_hot_backups WHERE session_id = ?`;
const SAVE_HOT_BACKUP_SQL = `
  INSERT INTO session_hot_backups (
    session_id, revision, backup_id, local, expires_at
  ) VALUES (?, ?, ?, ?, ?)
  ON CONFLICT(session_id) DO UPDATE SET
    revision = excluded.revision,
    backup_id = excluded.backup_id,
    local = excluded.local,
    expires_at = excluded.expires_at
`;

const PROJECT_BACKUP_SQL = `
  SELECT backup_id AS backupId, local, expires_at AS expiresAt
  FROM project_sandbox_backups
  WHERE controller_user_id = ? AND github_repository_id = ?
    AND base_commit_sha = ? AND image_version = ?
`;
const TOUCH_PROJECT_BACKUP_SQL = `
  UPDATE project_sandbox_backups SET last_used_at = ?
  WHERE controller_user_id = ? AND github_repository_id = ?
    AND base_commit_sha = ? AND image_version = ?
`;
const SAVE_PROJECT_BACKUP_SQL = `
  INSERT INTO project_sandbox_backups (
    controller_user_id, github_repository_id, base_commit_sha, image_version,
    backup_id, local, expires_at, last_used_at
  ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
  ON CONFLICT(
    controller_user_id, github_repository_id, base_commit_sha, image_version
  ) DO UPDATE SET
    backup_id = excluded.backup_id,
    local = excluded.local,
    expires_at = excluded.expires_at,
    last_used_at = excluded.last_used_at
`;
const EXCESS_PROJECT_BACKUPS_SQL = `
  SELECT
    controller_user_id AS controllerUserId,
    github_repository_id AS githubRepositoryId,
    base_commit_sha AS baseCommitSha,
    image_version AS imageVersion,
    backup_id AS backupId,
    local,
    expires_at AS expiresAt
  FROM project_sandbox_backups
  WHERE controller_user_id = ? AND github_repository_id = ? AND image_version = ?
  ORDER BY last_used_at DESC
  LIMIT -1 OFFSET ?
`;
const DELETE_PROJECT_BACKUP_SQL = `
  DELETE FROM project_sandbox_backups
  WHERE controller_user_id = ? AND github_repository_id = ?
    AND base_commit_sha = ? AND image_version = ?
`;

export type SessionAttempt = {
  id: string;
  turnId: string;
  turnOrdinal: number;
  idempotencyKey: string;
  workspaceId: string;
  controllerUserId: string;
  name: string;
  projectId: string | null;
  projectInstructions: string;
  githubRepositoryId: string;
  githubUserId: string;
  repositoryUrl: string;
  baseRef: string;
  baseCommitSha: string;
  publicationBranch: string;
  revision: number;
  expectedCommitSha: string;
  gitObjectKey: string;
  piObjectKey: string;
  diffObjectKey: string;
  transcriptObjectKey: string;
  previousRevision?: StoredRevision;
};

export type StoredRevision = {
  revision: number;
  commitSha: string;
  gitObjectKey: string;
  gitSize: number;
  piObjectKey: string;
  piSize: number;
};

export type PublicationContext = StoredRevision & {
  id: string;
  controllerUserId: string;
  githubRepositoryId: string;
  githubUserId: string;
  repositoryUrl: string;
  baseRef: string;
  baseCommitSha: string;
  publicationBranch: string;
  publishedRevision: number | null;
  publishedCommitSha: string | null;
  pullRequestNumber: number | null;
  pullRequestUrl: string | null;
};

export type ProjectCacheKey = {
  controllerUserId: string;
  githubRepositoryId: string;
  baseCommitSha: string;
  imageVersion: string;
};

type CacheLookup = {
  backup?: SandboxBackup;
  stale: SandboxBackup[];
};

type AttemptInput = Omit<SessionAttempt,
  "id" | "gitObjectKey" | "piObjectKey" | "diffObjectKey" | "transcriptObjectKey">
  & { sessionId: string };

type StoredAttempt = {
  sessionId: string;
  workspaceId: string;
  controllerUserId: string;
  gitObjectKey: string;
  piObjectKey: string;
  turnId: string | null;
};

export type StoredSessionTurn = {
  id: string;
  ordinal: number;
  status: SessionTurnStatus;
  failureCode: string | null;
  resultRevision: number | null;
  transcriptObjectKey: string;
  transcriptSize: number | null;
  lastEventSequence: number | null;
  createdAt: string;
  completedAt: string | null;
};

type StoredSessionDetails = {
  id: string;
  workspaceId: string;
  controllerUserId: string;
  name: string;
  projectId: string | null;
  githubRepositoryId: string;
  currentRevision: number | null;
  publicationBranch: string;
  publishedRevision: number | null;
  publishedCommitSha: string | null;
  pullRequestNumber: number | null;
  pullRequestUrl: string | null;
  createdAt: string;
  updatedAt: string;
};

type StoredSession = {
  id: string;
  controllerUserId: string;
  name: string;
  projectId: string | null;
  githubRepositoryId: string;
  githubUserId: string;
  repositoryUrl: string;
  baseRef: string;
  baseCommitSha: string;
  publicationBranch: string;
  revision: number | null;
  commitSha: string | null;
  gitObjectKey: string | null;
  gitSize: number | null;
  piObjectKey: string | null;
  piSize: number | null;
};

type StoredBackup = {
  revision: number;
  backupId: string;
  local: number;
  expiresAt: number;
};

type StoredProjectBackup = StoredBackup & ProjectCacheKey;

type SessionStoreErrorCode =
  | "not_found"
  | "not_controller"
  | "revision_limit"
  | "turn_limit"
  | "session_limit"
  | "snapshot_missing"
  | "artifact_missing"
  | "stored_snapshot_too_large"
  | "stored_artifact_too_large"
  | "stored_transcript_invalid"
  | "stored_diff_invalid"
  | "diff_not_available"
  | "snapshot_too_large"
  | "artifact_too_large"
  | "transcript_too_large"
  | "conflict";
