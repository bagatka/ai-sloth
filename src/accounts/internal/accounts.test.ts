import { expect, test } from "bun:test";
import type { User } from "../authentication";
import { createAccountOperations } from "./accounts";
import type {
  AccountRepository,
  NewAccount,
  StoredCredential,
} from "./store";
import type { NewAccountSession } from "./token";

class MemoryAccounts implements AccountRepository {
  private readonly credentials = new Map<string, StoredCredential>();
  private readonly sessions = new Map<
    string,
    { user: User; expiresAt: number }
  >();

  async createAccount(account: NewAccount): Promise<void> {
    if (this.credentials.has(account.user.normalizedEmail)) {
      throw new Error("UNIQUE constraint failed: users.normalized_email");
    }
    const user = { id: account.user.id, email: account.user.email };
    this.credentials.set(account.user.normalizedEmail, {
      ...user,
      normalizedEmail: account.user.normalizedEmail,
      credential: account.credential,
    });
    this.storeSession(user, account.session);
  }

  async findCredential(email: string): Promise<StoredCredential | null> {
    return this.credentials.get(email) ?? null;
  }

  async createSession(
    userId: string,
    session: NewAccountSession,
  ): Promise<void> {
    const user = [...this.credentials.values()].find(
      (credential) => credential.id === userId,
    );
    if (!user) {
      throw new Error("User not found");
    }
    this.storeSession(user, session);
  }

  async findUser(tokenHash: string, now: number): Promise<User | null> {
    const session = this.sessions.get(tokenHash);
    return session && session.expiresAt > now ? session.user : null;
  }

  async deleteSession(tokenHash: string): Promise<void> {
    this.sessions.delete(tokenHash);
  }

  private storeSession(user: User, session: NewAccountSession): void {
    this.sessions.set(session.tokenHash, {
      user: { id: user.id, email: user.email },
      expiresAt: session.expiresAt,
    });
  }
}

test("registers, authenticates, logs in, and logs out an account", async () => {
  const accounts = createAccountOperations(new MemoryAccounts());
  const registration = await accounts.register({
    email: "  User@Example.com ",
    password: "correct horse battery staple",
  });

  expect(registration.ok).toBeTrue();
  if (!registration.ok) {
    return;
  }
  expect(registration.value.user.email).toBe("User@Example.com");
  expect(registration.value.sessionToken).toMatch(/^asl_session_/);
  expect(await accounts.authenticate(registration.value.sessionToken)).toEqual({
    ok: true,
    value: {
      actor: { userId: registration.value.user.id },
      user: registration.value.user,
    },
  });

  const login = await accounts.login({
    email: "user@example.com",
    password: "correct horse battery staple",
  });
  expect(login.ok).toBeTrue();
  if (login.ok) {
    expect(login.value.user).toEqual(registration.value.user);
  }

  await accounts.logout(registration.value.sessionToken);
  expect(await accounts.authenticate(registration.value.sessionToken)).toEqual({
    ok: false,
    code: "invalid_session",
  });
});

test("does not reveal whether login email or password was wrong", async () => {
  const accounts = createAccountOperations(new MemoryAccounts());

  expect(await accounts.login({
    email: "missing@example.com",
    password: "incorrect password",
  })).toEqual({ ok: false, code: "invalid_credentials" });
});
