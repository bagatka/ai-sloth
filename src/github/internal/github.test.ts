import { expect, test } from "bun:test";
import { createGitHubOperations } from "./github";
import type {
  GitHubRepositoryStore,
  NewConnectionFlow,
  StoredConnectionFlow,
  StoredGitHubConnection,
} from "./store";

const USER_ID = "a47f6e35-b7f3-4c6f-91f6-93f0479ec15b";
const ENCRYPTION_KEY = btoa(String.fromCharCode(...new Uint8Array(32).fill(7)))
  .replaceAll("+", "-")
  .replaceAll("/", "_")
  .replace(/=+$/, "");

test("connects one GitHub identity through a one-use PKCE flow", async () => {
  const store = new MemoryStore();
  const github = createGitHubOperations(store, {
    clientId: "client-id",
    clientSecret: "client-secret",
    tokenEncryptionKey: ENCRYPTION_KEY,
  });
  const started = await github.startConnection(
    USER_ID,
    "https://api.example.com/github/callback",
  );
  expect(started.ok).toBeTrue();
  if (!started.ok) return;
  const authorization = new URL(started.value.authorizationUrl);
  expect(authorization.hostname).toBe("github.com");
  expect(authorization.searchParams.get("code_challenge_method")).toBe("S256");
  const state = authorization.searchParams.get("state")!;

  const originalFetch = globalThis.fetch;
  globalThis.fetch = (async (input: RequestInfo | URL) => {
    const url = new URL(input.toString());
    if (url.pathname === "/login/oauth/access_token") {
      return Response.json({
        access_token: "ghu_access",
        expires_in: 28_800,
        refresh_token: "ghr_refresh",
        refresh_token_expires_in: 15_897_600,
      });
    }
    if (url.pathname === "/user") {
      return Response.json({ id: 1234, login: "alice" });
    }
    if (url.pathname === "/user/installations") {
      return Response.json({ total_count: 1, installations: [{ id: 55 }] });
    }
    if (url.pathname === "/user/installations/55/repositories") {
      return Response.json({
        total_count: 1,
        repositories: [{
          id: 1296269,
          name: "repo",
          full_name: "alice/repo",
          owner: { login: "alice" },
          default_branch: "main",
          private: true,
          permissions: { push: true },
        }],
      });
    }
    if (url.pathname === "/repositories/1296269") {
      return Response.json({
        id: 1296269,
        name: "repo",
        full_name: "alice/repo",
        owner: { login: "alice" },
        default_branch: "main",
        private: true,
        permissions: { push: true },
      });
    }
    throw new Error(`Unexpected GitHub request: ${url}`);
  }) as typeof fetch;

  try {
    expect(await github.completeConnection(state, "authorization-code-123"))
      .toEqual({
        ok: true,
        value: { githubUserId: "1234", login: "alice" },
      });
    expect(store.connection?.accessToken).not.toContain("ghu_access");
    expect(await github.listRepositories(USER_ID, null)).toEqual({
      ok: true,
      value: {
        items: [{
          id: "1296269",
          installationId: "55",
          name: "repo",
          owner: "alice",
          fullName: "alice/repo",
          defaultBranch: "main",
          private: true,
          canPush: true,
        }],
        previousCursor: null,
        nextCursor: null,
      },
    });
    expect(await github.getRepositoryAccess(USER_ID, "1296269")).toEqual({
      ok: true,
      value: {
        id: "1296269",
        installationId: null,
        name: "repo",
        owner: "alice",
        fullName: "alice/repo",
        defaultBranch: "main",
        private: true,
        canPush: true,
        githubUserId: "1234",
        cloneUrl: "https://github.com/alice/repo.git",
        accessToken: "ghu_access",
      },
    });
    expect(
      await github.getRepositoryAccess(USER_ID, "1296269", "9999"),
    ).toEqual({ ok: false, code: "access_denied" });
    expect(await github.completeConnection(state, "authorization-code-123"))
      .toEqual({ ok: false, code: "access_denied" });
  } finally {
    globalThis.fetch = originalFetch;
  }
});

class MemoryStore implements GitHubRepositoryStore {
  private flow: NewConnectionFlow | null = null;
  connection: StoredGitHubConnection | null = null;

  async createFlow(flow: NewConnectionFlow): Promise<void> {
    this.flow = flow;
  }

  async consumeFlow(
    stateHash: string,
    now: number,
  ): Promise<StoredConnectionFlow | null> {
    if (
      !this.flow
      || this.flow.stateHash !== stateHash
      || this.flow.expiresAt <= now
    ) {
      return null;
    }
    const flow = this.flow;
    this.flow = null;
    return {
      userId: flow.userId,
      callbackUrl: flow.callbackUrl,
      codeVerifier: flow.codeVerifier,
    };
  }

  async saveConnection(connection: StoredGitHubConnection): Promise<void> {
    this.connection = connection;
  }

  async findConnection(userId: string): Promise<StoredGitHubConnection | null> {
    return this.connection?.userId === userId ? this.connection : null;
  }

  async deleteConnection(): Promise<void> {
    this.connection = null;
  }

  async replaceTokens(
    userId: string,
    accessToken: string,
    accessTokenExpiresAt: number,
    refreshToken: string | null,
    refreshTokenExpiresAt: number | null,
  ): Promise<void> {
    if (!this.connection || this.connection.userId !== userId) return;
    this.connection = {
      ...this.connection,
      accessToken,
      accessTokenExpiresAt,
      refreshToken,
      refreshTokenExpiresAt,
    };
  }
}
