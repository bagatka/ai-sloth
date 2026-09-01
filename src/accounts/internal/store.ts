import type { User } from "../authentication";
import type { PasswordCredential } from "./password";
import type { NewAccountSession } from "./token";

const MAX_SESSIONS_PER_USER = 10;

export type StoredCredential = User & {
  normalizedEmail: string;
  credential: PasswordCredential;
};

export type NewAccount = {
  user: User & { normalizedEmail: string };
  credential: PasswordCredential;
  session: NewAccountSession;
};

export interface AccountRepository {
  createAccount(account: NewAccount): Promise<void>;
  findCredential(normalizedEmail: string): Promise<StoredCredential | null>;
  createSession(userId: string, session: NewAccountSession): Promise<void>;
  findUser(tokenHash: string, now: number): Promise<User | null>;
  deleteSession(tokenHash: string): Promise<void>;
}

export class D1AccountRepository implements AccountRepository {
  constructor(private readonly database: D1Database) {}

  async createAccount(account: NewAccount): Promise<void> {
    const writes = await this.database.batch([
      this.database.prepare(INSERT_USER_SQL).bind(
        account.user.id,
        account.user.email,
        account.user.normalizedEmail,
        account.credential.hash,
        account.credential.salt,
        account.credential.algorithm,
        account.credential.iterations,
      ),
      this.sessionInsert(account.user.id, account.session),
    ]);
    requireSuccessfulWrites(writes, "Could not create account");
  }

  async findCredential(
    normalizedEmail: string,
  ): Promise<StoredCredential | null> {
    const stored = await this.database
      .prepare(FIND_CREDENTIAL_SQL)
      .bind(normalizedEmail)
      .first<StoredCredentialRow>();
    if (!stored) {
      return null;
    }

    return {
      id: stored.id,
      email: stored.email,
      normalizedEmail: stored.normalizedEmail,
      credential: {
        hash: stored.passwordHash,
        salt: stored.passwordSalt,
        algorithm: stored.passwordAlgorithm,
        iterations: stored.passwordIterations,
      },
    };
  }

  async createSession(
    userId: string,
    session: NewAccountSession,
  ): Promise<void> {
    const writes = await this.database.batch([
      this.database.prepare(DELETE_EXPIRED_SESSIONS_SQL).bind(
        userId,
        session.createdAt,
      ),
      this.database.prepare(TRIM_SESSIONS_SQL).bind(
        userId,
        userId,
        MAX_SESSIONS_PER_USER - 1,
      ),
      this.sessionInsert(userId, session),
    ]);
    requireSuccessfulWrites(writes, "Could not create account session");
  }

  findUser(tokenHash: string, now: number): Promise<User | null> {
    return this.database
      .prepare(FIND_USER_SQL)
      .bind(tokenHash, now)
      .first<User>();
  }

  async deleteSession(tokenHash: string): Promise<void> {
    const write = await this.database
      .prepare(DELETE_SESSION_SQL)
      .bind(tokenHash)
      .run();
    if (!write.success) {
      throw new Error("Could not delete account session");
    }
  }

  private sessionInsert(
    userId: string,
    session: NewAccountSession,
  ): D1PreparedStatement {
    return this.database.prepare(INSERT_SESSION_SQL).bind(
      session.tokenHash,
      userId,
      session.expiresAt,
      session.createdAt,
    );
  }
}

export function isDuplicateEmailError(error: unknown): boolean {
  return error instanceof Error
    && error.message.includes("UNIQUE constraint failed: users.normalized_email");
}

function requireSuccessfulWrites(
  writes: D1Result[],
  message: string,
): void {
  if (writes.some((write) => !write.success)) {
    throw new Error(message);
  }
}

type StoredCredentialRow = {
  id: string;
  email: string;
  normalizedEmail: string;
  passwordHash: string;
  passwordSalt: string;
  passwordAlgorithm: string;
  passwordIterations: number;
};

const INSERT_USER_SQL = `
  INSERT INTO users (
    id,
    email,
    normalized_email,
    password_hash,
    password_salt,
    password_algorithm,
    password_iterations
  ) VALUES (?, ?, ?, ?, ?, ?, ?)
`;

const INSERT_SESSION_SQL = `
  INSERT INTO account_sessions (token_hash, user_id, expires_at, created_at)
  VALUES (?, ?, ?, ?)
`;

const FIND_CREDENTIAL_SQL = `
  SELECT
    id,
    email,
    normalized_email AS normalizedEmail,
    password_hash AS passwordHash,
    password_salt AS passwordSalt,
    password_algorithm AS passwordAlgorithm,
    password_iterations AS passwordIterations
  FROM users
  WHERE normalized_email = ?
`;

const DELETE_EXPIRED_SESSIONS_SQL = `
  DELETE FROM account_sessions
  WHERE user_id = ? AND expires_at <= ?
`;

const TRIM_SESSIONS_SQL = `
  DELETE FROM account_sessions
  WHERE user_id = ? AND token_hash NOT IN (
    SELECT token_hash
    FROM account_sessions
    WHERE user_id = ?
    ORDER BY created_at DESC
    LIMIT ?
  )
`;

const FIND_USER_SQL = `
  SELECT users.id, users.email
  FROM account_sessions
  JOIN users ON users.id = account_sessions.user_id
  WHERE account_sessions.token_hash = ? AND account_sessions.expires_at > ?
`;

const DELETE_SESSION_SQL = `
  DELETE FROM account_sessions
  WHERE token_hash = ?
`;
