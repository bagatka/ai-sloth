import type { RepositorySource } from "./git";

const MAX_SESSION_BYTES = 16 * 1024 * 1024;
const MAX_SESSION_REVISIONS = 100;

export class SessionStore {
  constructor(
    private readonly database: D1Database,
    private readonly snapshots: R2Bucket,
  ) {}

  start(repositoryUrl: string, branch: string): SessionAttempt {
    return {
      kind: "new",
      id: crypto.randomUUID(),
      revision: 1,
      repository: { repositoryUrl, branch },
    };
  }

  async resume(sessionId: string): Promise<SessionAttempt> {
    const stored = await this.loadLatest(sessionId);
    if (!stored) {
      throw new SessionStoreError("not_found");
    }
    if (stored.revision >= MAX_SESSION_REVISIONS) {
      throw new SessionStoreError("revision_limit");
    }

    return {
      kind: "continued",
      id: stored.id,
      revision: stored.revision + 1,
      repository: {
        repositoryUrl: stored.repositoryUrl,
        commitSha: stored.commitSha,
      },
    };
  }

  async restore(
    session: SessionAttempt,
  ): Promise<ReadableStream<Uint8Array> | undefined> {
    if (session.kind === "new") {
      return undefined;
    }

    const stored = await this.database
      .prepare(SESSION_SNAPSHOT_SQL)
      .bind(session.id, session.revision - 1)
      .first<StoredSnapshot>();
    if (!stored) {
      throw new SessionStoreError("snapshot_missing");
    }

    const snapshot = await this.snapshots.get(stored.objectKey);
    if (!snapshot) {
      throw new SessionStoreError("snapshot_missing");
    }
    if (snapshot.size > MAX_SESSION_BYTES) {
      throw new SessionStoreError("stored_snapshot_too_large");
    }

    return snapshot.body;
  }

  async commit(
    session: SessionAttempt,
    commitSha: string,
    snapshot: PiSessionSnapshot,
  ): Promise<void> {
    if (snapshot.size > MAX_SESSION_BYTES) {
      throw new SessionStoreError("snapshot_too_large");
    }

    const objectKey = createSnapshotKey(session);
    const fixedLengthContent = snapshot.content.pipeThrough(
      new FixedLengthStream(snapshot.size),
    );
    await this.snapshots.put(objectKey, fixedLengthContent, {
      httpMetadata: { contentType: "application/x-ndjson" },
    });

    try {
      await this.writeMetadata(session, commitSha, objectKey);
    } catch (error) {
      await this.deleteSnapshot(objectKey);
      if (
        session.kind === "continued"
        && await this.hasRevision(session.id, session.revision)
      ) {
        throw new SessionStoreError("conflict");
      }
      throw error;
    }
  }

  private async loadLatest(sessionId: string): Promise<StoredSession | null> {
    return this.database
      .prepare(LATEST_SESSION_SQL)
      .bind(sessionId)
      .first<StoredSession>();
  }

  private async writeMetadata(
    session: SessionAttempt,
    commitSha: string,
    objectKey: string,
  ): Promise<void> {
    if (session.kind === "new") {
      const writes = await this.database.batch([
        this.database.prepare(INSERT_SESSION_SQL).bind(
          session.id,
          session.repository.repositoryUrl,
          commitSha,
        ),
        this.snapshotInsert(session, objectKey),
      ]);
      if (writes.some((write) => !write.success)) {
        throw new Error("Could not store new session metadata");
      }
      return;
    }

    const write = await this.snapshotInsert(session, objectKey).run();
    if (!write.success) {
      throw new Error("Could not store session snapshot metadata");
    }
  }

  private snapshotInsert(
    session: SessionAttempt,
    objectKey: string,
  ): D1PreparedStatement {
    return this.database
      .prepare(INSERT_SNAPSHOT_SQL)
      .bind(session.id, session.revision, objectKey);
  }

  private async hasRevision(
    sessionId: string,
    revision: number,
  ): Promise<boolean> {
    const stored = await this.database
      .prepare(HAS_REVISION_SQL)
      .bind(sessionId, revision)
      .first();
    return stored !== null;
  }

  private async deleteSnapshot(objectKey: string): Promise<void> {
    try {
      await this.snapshots.delete(objectKey);
    } catch (error) {
      console.error(
        "Could not delete uncommitted session snapshot",
        error instanceof Error ? error.message : "Unknown error",
      );
    }
  }
}

export class SessionStoreError extends Error {
  constructor(readonly code: SessionStoreErrorCode) {
    super(code);
  }
}

function createSnapshotKey(session: SessionAttempt): string {
  const revision = session.revision.toString().padStart(8, "0");
  return `sessions/${session.id}/${revision}-${crypto.randomUUID()}.jsonl`;
}

const LATEST_SESSION_SQL = `
  SELECT
    sessions.id,
    sessions.repository_url AS repositoryUrl,
    sessions.commit_sha AS commitSha,
    session_snapshots.revision
  FROM sessions
  JOIN session_snapshots ON session_snapshots.session_id = sessions.id
  WHERE sessions.id = ?
  ORDER BY session_snapshots.revision DESC
  LIMIT 1
`;

const SESSION_SNAPSHOT_SQL = `
  SELECT object_key AS objectKey
  FROM session_snapshots
  WHERE session_id = ? AND revision = ?
`;

const INSERT_SESSION_SQL = `
  INSERT INTO sessions (id, repository_url, commit_sha)
  VALUES (?, ?, ?)
`;

const INSERT_SNAPSHOT_SQL = `
  INSERT INTO session_snapshots (session_id, revision, object_key)
  VALUES (?, ?, ?)
`;

const HAS_REVISION_SQL = `
  SELECT 1
  FROM session_snapshots
  WHERE session_id = ? AND revision = ?
`;

export type SessionAttempt =
  | {
      kind: "new";
      id: string;
      revision: 1;
      repository: Extract<RepositorySource, { branch: string }>;
    }
  | {
      kind: "continued";
      id: string;
      revision: number;
      repository: Extract<RepositorySource, { commitSha: string }>;
    };

export type PiSessionSnapshot = {
  content: ReadableStream<Uint8Array>;
  size: number;
};

type StoredSession = {
  id: string;
  repositoryUrl: string;
  commitSha: string;
  revision: number;
};

type StoredSnapshot = {
  objectKey: string;
};

type SessionStoreErrorCode =
  | "not_found"
  | "revision_limit"
  | "snapshot_missing"
  | "stored_snapshot_too_large"
  | "snapshot_too_large"
  | "conflict";
