import type { SessionOperations } from "@ai-sloth/sessions";
import { expect, test } from "bun:test";
import { createApp } from "../app";
import type { ApiBindings } from "../internal/environment";
import { isSessionId } from "../internal/sessions/request";

const USER_ID = "a47f6e35-b7f3-4c6f-91f6-93f0479ec15b";
const WORKSPACE_ID = "c47f6e35-b7f3-4c6f-91f6-93f0479ec15b";
const SESSION_ID = "b47f6e35-b7f3-4c6f-91f6-93f0479ec15b";
const TURN_ID = "e47f6e35-b7f3-4c6f-91f6-93f0479ec15b";
const IDEMPOTENCY_KEY = "f47f6e35-b7f3-4c6f-91f6-93f0479ec15b";
const ACCOUNT_TOKEN = `asl_session_${"a".repeat(43)}`;

const failingSessions: SessionOperations = {
  async start() {
    throw new Error("Session start should not be called");
  },
  async continue() {
    throw new Error("Session continuation should not be called");
  },
  async get() {
    throw new Error("Session get should not be called");
  },
  async diff() {
    throw new Error("Session diff should not be called");
  },
  async discard() {
    throw new Error("Discard should not be called");
  },
  async publish() {
    throw new Error("Publication should not be called");
  },
  async connectEvents() {
    return Response.json(
      { error: "Session event streaming is not implemented" },
      { status: 501 },
    );
  },
};
const app = createApp();
const CONFIGURED_ENV = configuredEnvironment();

function configuredEnvironment(
  overrides: Partial<SessionOperations> = {},
): ApiBindings {
  const sessions = { ...failingSessions, ...overrides };
  const sessionNamespace = {
    getByName(sessionId: string) {
      return {
        start: sessions.start,
        continue: sessions.continue,
        get: sessions.get,
        diff: sessions.diff,
        discard: sessions.discard,
        publish: sessions.publish,
        fetch: (request: Request) => sessions.connectEvents({
          sessionId,
          workspaceId: WORKSPACE_ID,
          controllerUserId: USER_ID,
        }, request),
      };
    },
  } as unknown as ApiBindings["SESSION_COORDINATORS"];
  const accountsDatabase = {
    prepare() {
      return {
        bind() {
          return {
            async first() {
              return { id: USER_ID, email: "user@example.com" };
            },
          };
        },
      };
    },
  } as unknown as D1Database;
  const sessionDatabase = {
    prepare() {
      return {
        bind() {
          return {
            async all() {
              return { success: true, results: [] };
            },
          };
        },
      };
    },
  } as unknown as D1Database;
  const workspacesDatabase = {
    prepare() {
      return {
        bind() {
          return {
            async first() {
              return { id: WORKSPACE_ID, name: "Example" };
            },
          };
        },
      };
    },
  } as unknown as D1Database;

  return {
    ACCOUNTS_DB: accountsDatabase,
    WORKSPACES_DB: workspacesDatabase,
    AUTH_RATE_LIMITER: {
      async limit() {
        return { success: true };
      },
    },
    OPENROUTER_API_KEY: "test-openrouter-key",
    SESSION_COORDINATORS: sessionNamespace,
    SESSION_DB: sessionDatabase,
    SESSION_RATE_LIMITER: {
      async limit() {
        return { success: true };
      },
    },
  } as unknown as ApiBindings;
}

const accountRequest = (body?: unknown): RequestInit => ({
  method: "POST",
  headers: {
    Authorization: `Bearer ${ACCOUNT_TOKEN}`,
    "Idempotency-Key": IDEMPOTENCY_KEY,
  },
  ...(body === undefined ? {} : { body: JSON.stringify(body) }),
});

const sessionUrl = (suffix = "") =>
  `https://example.com/workspaces/${WORKSPACE_ID}/sessions${suffix}`;

test("workspace repositories load durable project items", async () => {
  const response = await app.request(
    `https://example.com/workspaces/${WORKSPACE_ID}/repositories/1296269/items`,
    { headers: { Authorization: `Bearer ${ACCOUNT_TOKEN}` } },
    CONFIGURED_ENV,
  );

  expect(response.status).toBe(200);
  expect(await response.text()).toBe(
    '{"items":[],"previousCursor":null,"nextCursor":null}',
  );
});

test("unknown routes return a JSON 404 response", async () => {
  const response = await app.request("https://example.com/unknown");

  expect(response.status).toBe(404);
  expect(response.headers.get("Content-Type")).toContain("application/json");
  expect(await response.text()).toBe('{"error":"Not found"}');
});

test("authenticated session routes require configured agent service", async () => {
  const response = await app.request(
    sessionUrl(),
    accountRequest({}),
    { ...CONFIGURED_ENV, OPENROUTER_API_KEY: "" },
  );

  expect(response.status).toBe(503);
  expect(await response.text()).toBe('{"error":"Service is not configured"}');
});

test("session routes authenticate before exposing service configuration", async () => {
  const response = await app.request(
    sessionUrl(),
    { method: "POST", body: "{}" },
    { ...CONFIGURED_ENV, OPENROUTER_API_KEY: "" },
  );

  expect(response.status).toBe(401);
  expect(await response.text()).toBe('{"error":"Unauthorized"}');
});

test("session operations enforce their account rate limit", async () => {
  let rateLimitKey: string | undefined;
  const response = await app.request(
    sessionUrl(),
    accountRequest({}),
    {
      ...CONFIGURED_ENV,
      SESSION_RATE_LIMITER: {
        async limit(input: { key: string }) {
          rateLimitKey = input.key;
          return { success: false };
        },
      },
    },
  );

  expect(response.status).toBe(429);
  expect(rateLimitKey).toBe(USER_ID);
  expect(await response.text()).toBe(
    '{"error":"Too many session requests"}',
  );
});

test("new sessions validate their request body", async () => {
  const response = await app.request(
    sessionUrl(),
    accountRequest({}),
    CONFIGURED_ENV,
  );

  expect(response.status).toBe(400);
  expect(await response.text()).toBe(
    '{"error":"Expected an idempotency key, GitHub repository ID, branch, and non-empty prompt"}',
  );
});

test("continued sessions validate the session ID", async () => {
  const response = await app.request(
    sessionUrl("/not-a-session/messages"),
    accountRequest({ prompt: "Continue" }),
    CONFIGURED_ENV,
  );

  expect(response.status).toBe(400);
  expect(await response.text()).toBe(
    '{"error":"Expected an idempotency key, valid session ID, and non-empty prompt"}',
  );
});

test("new sessions invoke workspace-scoped session operations", async () => {
  let invocation: Parameters<SessionOperations["start"]>[0] | undefined;
  const environment = configuredEnvironment({
    async start(input) {
      invocation = input;
      return {
        ok: true,
        value: {
          sessionId: input.sessionId,
          turnId: TURN_ID,
          status: "running",
        },
      };
    },
    continue: failingSessions.continue,
    discard: failingSessions.discard,
    publish: failingSessions.publish,
    connectEvents: failingSessions.connectEvents,
  });
  const response = await app.request(
    sessionUrl(),
    accountRequest({
      githubRepositoryId: "1296269",
      branch: "main",
      prompt: "Review it",
    }),
    environment,
  );

  expect(response.status).toBe(201);
  expect(invocation).toBeDefined();
  expect(isSessionId(invocation!.sessionId)).toBeTrue();
  expect(invocation).toEqual({
    sessionId: invocation!.sessionId,
    idempotencyKey: IDEMPOTENCY_KEY,
    workspaceId: WORKSPACE_ID,
    controllerUserId: USER_ID,
    name: "Review it",
    projectId: null,
    githubRepositoryId: "1296269",
    branch: "main",
    prompt: "Review it",
  });
});

test("continued sessions remain scoped to their workspace", async () => {
  let invocation: unknown;
  const environment = configuredEnvironment({
    start: failingSessions.start,
    async continue(input) {
      invocation = input;
      return {
        ok: true,
        value: {
          sessionId: input.sessionId,
          turnId: TURN_ID,
          status: "running",
        },
      };
    },
    discard: failingSessions.discard,
    publish: failingSessions.publish,
    connectEvents: failingSessions.connectEvents,
  });
  const response = await app.request(
    sessionUrl(`/${SESSION_ID}/messages`),
    accountRequest({ prompt: "Continue" }),
    environment,
  );

  expect(response.status).toBe(202);
  expect(invocation).toEqual({
    sessionId: SESSION_ID,
    idempotencyKey: IDEMPOTENCY_KEY,
    workspaceId: WORKSPACE_ID,
    controllerUserId: USER_ID,
    prompt: "Continue",
  });
});

test("session details remain scoped to their workspace", async () => {
  let invocation: unknown;
  const environment = configuredEnvironment({
    async get(input) {
      invocation = input;
      return {
        ok: true,
        value: {
          id: SESSION_ID,
          name: "Review it",
          workspaceId: WORKSPACE_ID,
          githubRepositoryId: "1296269",
          projectId: null,
          status: "running",
          revision: null,
          publication: null,
          createdAt: "2026-08-30 10:00:00",
          updatedAt: "2026-08-30 10:00:00",
          turns: [],
        },
      };
    },
  });

  const response = await app.request(
    sessionUrl(`/${SESSION_ID}`),
    { headers: { Authorization: `Bearer ${ACCOUNT_TOKEN}` } },
    environment,
  );

  expect(response.status).toBe(200);
  expect(invocation).toEqual({
    sessionId: SESSION_ID,
    workspaceId: WORKSPACE_ID,
    controllerUserId: USER_ID,
  });
});

test("session diff remains scoped to its workspace", async () => {
  let invocation: unknown;
  const patch = "diff --git a/file.txt b/file.txt\n";
  const environment = configuredEnvironment({
    async diff(input) {
      invocation = input;
      return {
        ok: true,
        value: {
          revision: 2,
          size: Buffer.byteLength(patch),
          content: new Blob([patch]).stream(),
        },
      };
    },
  });

  const response = await app.request(
    sessionUrl(`/${SESSION_ID}/diff`),
    { headers: { Authorization: `Bearer ${ACCOUNT_TOKEN}` } },
    environment,
  );

  expect(response.status).toBe(200);
  expect(response.headers.get("Content-Type")).toContain("text/x-diff");
  expect(response.headers.get("X-Session-Revision")).toBe("2");
  expect(await response.text()).toBe(patch);
  expect(invocation).toEqual({
    sessionId: SESSION_ID,
    workspaceId: WORKSPACE_ID,
    controllerUserId: USER_ID,
  });
});

test("sessions are published by the session controller", async () => {
  let invocation: unknown;
  const environment = configuredEnvironment({
    start: failingSessions.start,
    continue: failingSessions.continue,
    discard: failingSessions.discard,
    async publish(input) {
      invocation = input;
      return {
        ok: true,
        value: {
          revision: 2,
          commitSha: "0123456789abcdef0123456789abcdef01234567",
          branch: `ai-sloth/${input.sessionId}`,
          pullRequest: {
            number: 42,
            url: "https://github.com/owner/repo/pull/42",
          },
        },
      };
    },
    connectEvents: failingSessions.connectEvents,
  });
  const response = await app.request(
    sessionUrl(`/${SESSION_ID}/publish`),
    accountRequest(),
    environment,
  );

  expect(response.status).toBe(200);
  expect(invocation).toEqual({
    sessionId: SESSION_ID,
    workspaceId: WORKSPACE_ID,
    controllerUserId: USER_ID,
  });
});

test("sessions can be discarded by their controller", async () => {
  let invocation: unknown;
  const environment = configuredEnvironment({
    start: failingSessions.start,
    continue: failingSessions.continue,
    async discard(input) {
      invocation = input;
      return { ok: true, value: undefined };
    },
    publish: failingSessions.publish,
    connectEvents: failingSessions.connectEvents,
  });
  const response = await app.request(
    sessionUrl(`/${SESSION_ID}`),
    { ...accountRequest(), method: "DELETE" },
    environment,
  );

  expect(response.status).toBe(204);
  expect(invocation).toEqual({
    sessionId: SESSION_ID,
    workspaceId: WORKSPACE_ID,
    controllerUserId: USER_ID,
  });
});

test("session event endpoint is delegated to its coordinator", async () => {
  const response = await app.request(
    sessionUrl(`/${SESSION_ID}/turns/${TURN_ID}/events`),
    { headers: { Authorization: `Bearer ${ACCOUNT_TOKEN}` } },
    CONFIGURED_ENV,
  );

  expect(response.status).toBe(501);
});

test("registration validates its request body", async () => {
  const response = await app.request(
    "https://example.com/auth/register",
    { method: "POST", body: "{}" },
    CONFIGURED_ENV,
  );

  expect(response.status).toBe(400);
  expect(await response.text()).toBe(
    '{"error":"Expected email and password"}',
  );
});

test("account endpoints require a bearer token", async () => {
  const response = await app.request(
    "https://example.com/auth/me",
    undefined,
    CONFIGURED_ENV,
  );

  expect(response.status).toBe(401);
});

test("authentication endpoints enforce their rate limit", async () => {
  const environment = {
    ...CONFIGURED_ENV,
    AUTH_RATE_LIMITER: {
      async limit() {
        return { success: false };
      },
    },
  } as ApiBindings;
  const response = await app.request(
    "https://example.com/auth/login",
    { method: "POST", body: "{}" },
    environment,
  );

  expect(response.status).toBe(429);
});

test("GitHub endpoints require an account", async () => {
  const response = await app.request(
    "https://example.com/github/repositories",
    undefined,
    CONFIGURED_ENV,
  );

  expect(response.status).toBe(401);
});
