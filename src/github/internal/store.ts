const MAX_FLOWS_PER_USER = 3;

export type StoredConnectionFlow = {
  userId: string;
  callbackUrl: string;
  codeVerifier: string;
};

export type NewConnectionFlow = StoredConnectionFlow & {
  stateHash: string;
  createdAt: number;
  expiresAt: number;
};

export type StoredGitHubConnection = {
  userId: string;
  githubUserId: string;
  login: string;
  accessToken: string;
  accessTokenExpiresAt: number;
  refreshToken: string | null;
  refreshTokenExpiresAt: number | null;
};

export interface GitHubRepositoryStore {
  createFlow(flow: NewConnectionFlow): Promise<void>;
  consumeFlow(stateHash: string, now: number): Promise<StoredConnectionFlow | null>;
  saveConnection(connection: StoredGitHubConnection): Promise<void>;
  findConnection(userId: string): Promise<StoredGitHubConnection | null>;
  deleteConnection(userId: string): Promise<void>;
  replaceTokens(
    userId: string,
    accessToken: string,
    accessTokenExpiresAt: number,
    refreshToken: string | null,
    refreshTokenExpiresAt: number | null,
  ): Promise<void>;
}

export class D1GitHubRepositoryStore implements GitHubRepositoryStore {
  constructor(private readonly database: D1Database) {}

  async createFlow(flow: NewConnectionFlow): Promise<void> {
    const writes = await this.database.batch([
      this.database.prepare(DELETE_EXPIRED_FLOWS_SQL).bind(
        flow.userId,
        flow.createdAt,
      ),
      this.database.prepare(TRIM_FLOWS_SQL).bind(
        flow.userId,
        flow.userId,
        MAX_FLOWS_PER_USER - 1,
      ),
      this.database.prepare(INSERT_FLOW_SQL).bind(
        flow.stateHash,
        flow.userId,
        flow.callbackUrl,
        flow.codeVerifier,
        flow.expiresAt,
        flow.createdAt,
      ),
    ]);
    requireSuccessful(writes, "Could not create GitHub connection flow");
  }

  consumeFlow(
    stateHash: string,
    now: number,
  ): Promise<StoredConnectionFlow | null> {
    return this.database
      .prepare(CONSUME_FLOW_SQL)
      .bind(stateHash, now)
      .first<StoredConnectionFlow>();
  }

  async saveConnection(connection: StoredGitHubConnection): Promise<void> {
    const write = await this.database.prepare(SAVE_CONNECTION_SQL).bind(
      connection.userId,
      connection.githubUserId,
      connection.login,
      connection.accessToken,
      connection.accessTokenExpiresAt,
      connection.refreshToken,
      connection.refreshTokenExpiresAt,
    ).run();
    requireSuccessful([write], "Could not save GitHub connection");
  }

  findConnection(userId: string): Promise<StoredGitHubConnection | null> {
    return this.database
      .prepare(FIND_CONNECTION_SQL)
      .bind(userId)
      .first<StoredGitHubConnection>();
  }

  async deleteConnection(userId: string): Promise<void> {
    const write = await this.database
      .prepare(DELETE_CONNECTION_SQL)
      .bind(userId)
      .run();
    requireSuccessful([write], "Could not delete GitHub connection");
  }

  async replaceTokens(
    userId: string,
    accessToken: string,
    accessTokenExpiresAt: number,
    refreshToken: string | null,
    refreshTokenExpiresAt: number | null,
  ): Promise<void> {
    const write = await this.database.prepare(REPLACE_TOKENS_SQL).bind(
      accessToken,
      accessTokenExpiresAt,
      refreshToken,
      refreshTokenExpiresAt,
      userId,
    ).run();
    requireSuccessful([write], "Could not refresh GitHub connection");
  }
}

function requireSuccessful(writes: D1Result[], message: string): void {
  if (writes.some((write) => !write.success)) {
    throw new Error(message);
  }
}

const DELETE_EXPIRED_FLOWS_SQL = `
  DELETE FROM github_connection_flows
  WHERE user_id = ? AND expires_at <= ?
`;

const TRIM_FLOWS_SQL = `
  DELETE FROM github_connection_flows
  WHERE user_id = ? AND state_hash NOT IN (
    SELECT state_hash
    FROM github_connection_flows
    WHERE user_id = ?
    ORDER BY created_at DESC
    LIMIT ?
  )
`;

const INSERT_FLOW_SQL = `
  INSERT INTO github_connection_flows (
    state_hash,
    user_id,
    callback_url,
    code_verifier,
    expires_at,
    created_at
  ) VALUES (?, ?, ?, ?, ?, ?)
`;

const CONSUME_FLOW_SQL = `
  DELETE FROM github_connection_flows
  WHERE state_hash = ? AND expires_at > ?
  RETURNING
    user_id AS userId,
    callback_url AS callbackUrl,
    code_verifier AS codeVerifier
`;

const SAVE_CONNECTION_SQL = `
  INSERT INTO github_connections (
    user_id,
    github_user_id,
    login,
    access_token,
    access_token_expires_at,
    refresh_token,
    refresh_token_expires_at
  ) VALUES (?, ?, ?, ?, ?, ?, ?)
  ON CONFLICT(user_id) DO UPDATE SET
    github_user_id = excluded.github_user_id,
    login = excluded.login,
    access_token = excluded.access_token,
    access_token_expires_at = excluded.access_token_expires_at,
    refresh_token = excluded.refresh_token,
    refresh_token_expires_at = excluded.refresh_token_expires_at,
    updated_at = CURRENT_TIMESTAMP
`;

const FIND_CONNECTION_SQL = `
  SELECT
    user_id AS userId,
    github_user_id AS githubUserId,
    login,
    access_token AS accessToken,
    access_token_expires_at AS accessTokenExpiresAt,
    refresh_token AS refreshToken,
    refresh_token_expires_at AS refreshTokenExpiresAt
  FROM github_connections
  WHERE user_id = ?
`;

const DELETE_CONNECTION_SQL = `
  DELETE FROM github_connections
  WHERE user_id = ?
`;

const REPLACE_TOKENS_SQL = `
  UPDATE github_connections
  SET
    access_token = ?,
    access_token_expires_at = ?,
    refresh_token = ?,
    refresh_token_expires_at = ?,
    updated_at = CURRENT_TIMESTAMP
  WHERE user_id = ?
`;
