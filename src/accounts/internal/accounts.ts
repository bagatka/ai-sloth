import type {
  AccountOperations,
  AccountOutcome,
  AccountSession,
  User,
} from "../authentication";
import {
  DUMMY_PASSWORD_CREDENTIAL,
  hashPassword,
  verifyPassword,
} from "./password";
import {
  isDuplicateEmailError,
  type AccountRepository,
} from "./store";
import {
  createAccountSession,
  hashSessionToken,
  isSessionToken,
} from "./token";
import {
  normalizeLogin,
  normalizeRegistration,
} from "./validation";

export function createAccountOperations(
  accounts: AccountRepository,
): AccountOperations {
  return {
    async register(input) {
      const normalized = normalizeRegistration(input);
      if (!normalized) {
        return { ok: false, code: "invalid_input" };
      }

      try {
        const user: User = { id: crypto.randomUUID(), email: normalized.email };
        const session = await createAccountSession();
        await accounts.createAccount({
          user: { ...user, normalizedEmail: normalized.normalizedEmail },
          credential: await hashPassword(normalized.password),
          session,
        });
        return authenticatedSession(user, session);
      } catch (error) {
        if (isDuplicateEmailError(error)) {
          return { ok: false, code: "email_taken" };
        }
        return internalFailure("Account registration failed", error);
      }
    },

    async login(input) {
      const normalized = normalizeLogin(input);
      if (!normalized) {
        return { ok: false, code: "invalid_credentials" };
      }

      try {
        const stored = await accounts.findCredential(normalized.normalizedEmail);
        const valid = await verifyPassword(
          normalized.password,
          stored?.credential ?? DUMMY_PASSWORD_CREDENTIAL,
        );
        if (!stored || !valid) {
          return { ok: false, code: "invalid_credentials" };
        }

        const session = await createAccountSession();
        await accounts.createSession(stored.id, session);
        return authenticatedSession(
          { id: stored.id, email: stored.email },
          session,
        );
      } catch (error) {
        return internalFailure("Account login failed", error);
      }
    },

    async authenticate(token) {
      if (!isSessionToken(token)) {
        return { ok: false, code: "invalid_session" };
      }

      try {
        const user = await accounts.findUser(
          await hashSessionToken(token),
          Date.now(),
        );
        return user
          ? { ok: true, value: { actor: { userId: user.id }, user } }
          : { ok: false, code: "invalid_session" };
      } catch (error) {
        return internalFailure("Account authentication failed", error);
      }
    },

    async logout(token) {
      if (!isSessionToken(token)) {
        return { ok: true, value: undefined };
      }

      try {
        await accounts.deleteSession(await hashSessionToken(token));
        return { ok: true, value: undefined };
      } catch (error) {
        return internalFailure("Account logout failed", error);
      }
    },
  };
}

function authenticatedSession(
  user: User,
  session: Awaited<ReturnType<typeof createAccountSession>>,
): AccountOutcome<AccountSession> {
  return {
    ok: true,
    value: {
      actor: { userId: user.id },
      user,
      sessionToken: session.token,
      expiresAt: new Date(session.expiresAt).toISOString(),
    },
  };
}

function internalFailure<T>(
  message: string,
  error: unknown,
): AccountOutcome<T> {
  console.error(
    message,
    error instanceof Error ? error.message : "Unknown error",
  );
  return { ok: false, code: "internal_error" };
}
