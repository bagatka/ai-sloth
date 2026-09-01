import type {
  GitHubConnection,
  GitHubFailureCode,
  GitHubOperations,
  GitHubPullRequest,
  GitHubOutcome,
  GitHubRepositoryAccess,
  GitHubRepositoryPage,
} from "../github";
import {
  createAuthorizationUrl,
  createPullRequest,
  exchangeAuthorizationCode,
  getBranchHead,
  getCurrentUser,
  getRepository,
  GitHubApiError,
  listInstallationRepositories,
  listInstallations,
  refreshAccessToken,
  type OAuthTokens,
} from "./api";
import {
  createConnectionFlowSecrets,
  decryptSecret,
  encryptSecret,
  hashConnectionState,
  isEncryptionKey,
} from "./crypto";
import type {
  GitHubRepositoryStore,
  StoredGitHubConnection,
} from "./store";

const FLOW_LIFETIME_MS = 10 * 60 * 1000;
const TOKEN_REFRESH_MARGIN_MS = 5 * 60 * 1000;
const REPOSITORIES_PER_PAGE = 30;

export type GitHubConfiguration = {
  clientId: string;
  clientSecret: string;
  tokenEncryptionKey: string;
};

export function createGitHubOperations(
  store: GitHubRepositoryStore,
  configuration: GitHubConfiguration,
): GitHubOperations {
  return {
    startConnection: (userId, callbackUrl) =>
      startConnection(store, configuration, userId, callbackUrl),
    completeConnection: (state, code) =>
      completeConnection(store, configuration, state, code),
    getConnection: (userId) => getConnection(store, userId),
    disconnect: (userId) => disconnect(store, userId),
    listRepositories: (userId, cursor) =>
      listUserRepositories(store, configuration, userId, cursor),
    getRepositoryAccess: (userId, repositoryId, expectedGitHubUserId) =>
      getUserRepositoryAccess(
        store,
        configuration,
        userId,
        repositoryId,
        expectedGitHubUserId,
      ),
    getBranchHead: (userId, repositoryId, branch, expectedGitHubUserId) =>
      getUserBranchHead(
        store,
        configuration,
        userId,
        repositoryId,
        branch,
        expectedGitHubUserId,
      ),
    createPullRequest: (userId, repositoryId, input) =>
      createUserPullRequest(store, configuration, userId, repositoryId, input),
  };
}

async function startConnection(
  store: GitHubRepositoryStore,
  configuration: GitHubConfiguration,
  userId: string,
  callbackUrl: string,
): Promise<GitHubOutcome<{ authorizationUrl: string }>> {
  if (!isConfigured(configuration)) {
    return { ok: false, code: "not_configured" };
  }
  if (!isUserId(userId) || !isCallbackUrl(callbackUrl)) {
    return { ok: false, code: "invalid_input" };
  }

  try {
    const secrets = await createConnectionFlowSecrets();
    const createdAt = Date.now();
    await store.createFlow({
      userId,
      callbackUrl,
      stateHash: secrets.stateHash,
      codeVerifier: secrets.codeVerifier,
      createdAt,
      expiresAt: createdAt + FLOW_LIFETIME_MS,
    });
    return {
      ok: true,
      value: {
        authorizationUrl: createAuthorizationUrl({
          clientId: configuration.clientId,
          callbackUrl,
          state: secrets.state,
          codeChallenge: secrets.codeChallenge,
        }),
      },
    };
  } catch (error) {
    return internalFailure("Could not start GitHub connection", error);
  }
}

async function completeConnection(
  store: GitHubRepositoryStore,
  configuration: GitHubConfiguration,
  state: string,
  code: string,
): Promise<GitHubOutcome<GitHubConnection>> {
  if (!isConfigured(configuration)) {
    return { ok: false, code: "not_configured" };
  }
  if (!isOAuthValue(state) || !isOAuthValue(code)) {
    return { ok: false, code: "invalid_input" };
  }

  try {
    const flow = await store.consumeFlow(
      await hashConnectionState(state),
      Date.now(),
    );
    if (!flow) {
      return { ok: false, code: "access_denied" };
    }
    const tokens = await exchangeAuthorizationCode({
      clientId: configuration.clientId,
      clientSecret: configuration.clientSecret,
      code,
      callbackUrl: flow.callbackUrl,
      codeVerifier: flow.codeVerifier,
    });
    const connection = await getCurrentUser(tokens.accessToken);
    await store.saveConnection({
      userId: flow.userId,
      githubUserId: connection.githubUserId,
      login: connection.login,
      ...await encryptTokens(tokens, configuration.tokenEncryptionKey),
    });
    return { ok: true, value: connection };
  } catch (error) {
    if (isUniqueGitHubUserError(error)) {
      return { ok: false, code: "conflict" };
    }
    return githubFailure("Could not complete GitHub connection", error);
  }
}

async function getConnection(
  store: GitHubRepositoryStore,
  userId: string,
): Promise<GitHubOutcome<GitHubConnection>> {
  if (!isUserId(userId)) {
    return { ok: false, code: "invalid_input" };
  }
  try {
    const connection = await store.findConnection(userId);
    return connection
      ? {
        ok: true,
        value: {
          githubUserId: connection.githubUserId,
          login: connection.login,
        },
      }
      : { ok: false, code: "not_connected" };
  } catch (error) {
    return internalFailure("Could not read GitHub connection", error);
  }
}

async function disconnect(
  store: GitHubRepositoryStore,
  userId: string,
): Promise<GitHubOutcome<undefined>> {
  if (!isUserId(userId)) {
    return { ok: false, code: "invalid_input" };
  }
  try {
    await store.deleteConnection(userId);
    return { ok: true, value: undefined };
  } catch (error) {
    return internalFailure("Could not disconnect GitHub", error);
  }
}

async function listUserRepositories(
  store: GitHubRepositoryStore,
  configuration: GitHubConfiguration,
  userId: string,
  cursor: string | null,
): Promise<GitHubOutcome<GitHubRepositoryPage>> {
  const requested = parseCursor(cursor);
  if (!isConfigured(configuration)) {
    return { ok: false, code: "not_configured" };
  }
  if (!isUserId(userId) || requested === null) {
    return { ok: false, code: "invalid_input" };
  }

  try {
    const credential = await requireCredential(store, configuration, userId);
    if (!credential) {
      return { ok: false, code: "not_connected" };
    }
    const installations = await listInstallations(credential.accessToken);
    let installationIndex = requested.installationIndex;
    let requestedPage = requested.page;

    while (installationIndex >= 0 && installationIndex < installations.length) {
      const installationId = installations[installationIndex];
      let page = requestedPage === "last" ? 1 : requestedPage;
      let result = await listInstallationRepositories(
        credential.accessToken,
        installationId,
        page,
        REPOSITORIES_PER_PAGE,
      );
      const pages = Math.ceil(result.totalCount / REPOSITORIES_PER_PAGE);
      if (pages === 0) {
        installationIndex += requestedPage === "last" ? -1 : 1;
        requestedPage = requestedPage === "last" ? "last" : 1;
        continue;
      }
      page = requestedPage === "last" ? pages : Math.min(requestedPage, pages);
      if (page !== 1) {
        result = await listInstallationRepositories(
          credential.accessToken,
          installationId,
          page,
          REPOSITORIES_PER_PAGE,
        );
      }
      return {
        ok: true,
        value: {
          items: result.repositories,
          previousCursor: page > 1
            ? cursorFor(installationIndex, page - 1)
            : installationIndex > 0
            ? cursorFor(installationIndex - 1, "last")
            : null,
          nextCursor: page < pages
            ? cursorFor(installationIndex, page + 1)
            : installationIndex + 1 < installations.length
            ? cursorFor(installationIndex + 1, 1)
            : null,
        },
      };
    }
    return {
      ok: true,
      value: { items: [], previousCursor: null, nextCursor: null },
    };
  } catch (error) {
    return githubFailure("Could not list GitHub repositories", error);
  }
}

async function getUserRepositoryAccess(
  store: GitHubRepositoryStore,
  configuration: GitHubConfiguration,
  userId: string,
  repositoryId: string,
  expectedGitHubUserId?: string,
): Promise<GitHubOutcome<GitHubRepositoryAccess>> {
  if (!isConfigured(configuration)) {
    return { ok: false, code: "not_configured" };
  }
  if (!isUserId(userId) || !isRepositoryId(repositoryId)) {
    return { ok: false, code: "invalid_input" };
  }

  try {
    const credential = await requireCredential(store, configuration, userId);
    if (!credential) {
      return { ok: false, code: "not_connected" };
    }
    if (
      expectedGitHubUserId
      && credential.githubUserId !== expectedGitHubUserId
    ) {
      return { ok: false, code: "access_denied" };
    }
    const repository = await getRepository(credential.accessToken, repositoryId);
    return {
      ok: true,
      value: {
        ...repository,
        githubUserId: credential.githubUserId,
        cloneUrl: `https://github.com/${repository.fullName}.git`,
        accessToken: credential.accessToken,
      },
    };
  } catch (error) {
    return githubFailure("Could not access GitHub repository", error);
  }
}

async function getUserBranchHead(
  store: GitHubRepositoryStore,
  configuration: GitHubConfiguration,
  userId: string,
  repositoryId: string,
  branch: string,
  expectedGitHubUserId: string,
): Promise<GitHubOutcome<string | null>> {
  if (!isConfigured(configuration)) {
    return { ok: false, code: "not_configured" };
  }
  if (
    !isUserId(userId)
    || !isRepositoryId(repositoryId)
    || !isBranch(branch)
    || !isRepositoryId(expectedGitHubUserId)
  ) {
    return { ok: false, code: "invalid_input" };
  }
  try {
    const credential = await requireCredential(store, configuration, userId);
    if (!credential) return { ok: false, code: "not_connected" };
    if (credential.githubUserId !== expectedGitHubUserId) {
      return { ok: false, code: "access_denied" };
    }
    const repository = await getRepository(credential.accessToken, repositoryId);
    return {
      ok: true,
      value: await getBranchHead(credential.accessToken, repository, branch),
    };
  } catch (error) {
    return githubFailure("Could not read GitHub branch", error);
  }
}

async function createUserPullRequest(
  store: GitHubRepositoryStore,
  configuration: GitHubConfiguration,
  userId: string,
  repositoryId: string,
  input: {
    head: string;
    base: string;
    title: string;
    expectedGitHubUserId: string;
  },
): Promise<GitHubOutcome<GitHubPullRequest>> {
  if (!isConfigured(configuration)) {
    return { ok: false, code: "not_configured" } as const;
  }
  if (
    !isUserId(userId)
    || !isRepositoryId(repositoryId)
    || !isBranch(input.head)
    || !isBranch(input.base)
    || !isRepositoryId(input.expectedGitHubUserId)
    || input.title.trim().length === 0
    || input.title.length > 256
  ) {
    return { ok: false, code: "invalid_input" } as const;
  }
  try {
    const credential = await requireCredential(store, configuration, userId);
    if (!credential) return { ok: false, code: "not_connected" } as const;
    if (credential.githubUserId !== input.expectedGitHubUserId) {
      return { ok: false, code: "access_denied" };
    }
    const repository = await getRepository(credential.accessToken, repositoryId);
    if (!repository.canPush) return { ok: false, code: "access_denied" };
    return {
      ok: true,
      value: await createPullRequest(credential.accessToken, repository, input),
    } as const;
  } catch (error) {
    return githubFailure("Could not create GitHub pull request", error);
  }
}

async function requireCredential(
  store: GitHubRepositoryStore,
  configuration: GitHubConfiguration,
  userId: string,
): Promise<{ accessToken: string; githubUserId: string } | null> {
  const connection = await store.findConnection(userId);
  if (!connection) return null;
  const accessToken = connection.accessTokenExpiresAt
      > Date.now() + TOKEN_REFRESH_MARGIN_MS
    ? await decryptSecret(
      connection.accessToken,
      configuration.tokenEncryptionKey,
    )
    : await refreshConnection(store, configuration, connection);
  return { accessToken, githubUserId: connection.githubUserId };
}

async function refreshConnection(
  store: GitHubRepositoryStore,
  configuration: GitHubConfiguration,
  connection: StoredGitHubConnection,
): Promise<string> {
  if (
    !connection.refreshToken
    || !connection.refreshTokenExpiresAt
    || connection.refreshTokenExpiresAt <= Date.now()
  ) {
    throw new GitHubApiError(401, "GitHub connection expired");
  }
  const refreshToken = await decryptSecret(
    connection.refreshToken,
    configuration.tokenEncryptionKey,
  );
  const tokens = await refreshAccessToken({
    clientId: configuration.clientId,
    clientSecret: configuration.clientSecret,
    refreshToken,
  });
  const encrypted = await encryptTokens(
    tokens,
    configuration.tokenEncryptionKey,
  );
  await store.replaceTokens(
    connection.userId,
    encrypted.accessToken,
    encrypted.accessTokenExpiresAt,
    encrypted.refreshToken,
    encrypted.refreshTokenExpiresAt,
  );
  return tokens.accessToken;
}

async function encryptTokens(
  tokens: OAuthTokens,
  key: string,
): Promise<Pick<StoredGitHubConnection,
  | "accessToken"
  | "accessTokenExpiresAt"
  | "refreshToken"
  | "refreshTokenExpiresAt">> {
  return {
    accessToken: await encryptSecret(tokens.accessToken, key),
    accessTokenExpiresAt: tokens.accessTokenExpiresAt,
    refreshToken: await encryptSecret(tokens.refreshToken, key),
    refreshTokenExpiresAt: tokens.refreshTokenExpiresAt,
  };
}

type RepositoryCursor = {
  installationIndex: number;
  page: number | "last";
};

function parseCursor(value: string | null): RepositoryCursor | null {
  if (value === null) return { installationIndex: 0, page: 1 };
  const match = /^(0|[1-9][0-9]?)\.(last|[1-9][0-9]{0,3})$/.exec(value);
  if (!match) return null;
  const installationIndex = Number(match[1]);
  const page = match[2] === "last" ? "last" : Number(match[2]);
  return { installationIndex, page };
}

function cursorFor(
  installationIndex: number,
  page: number | "last",
): string {
  return `${installationIndex}.${page}`;
}

function isConfigured(configuration: GitHubConfiguration): boolean {
  return configuration.clientId.length > 0
    && configuration.clientSecret.length > 0
    && isEncryptionKey(configuration.tokenEncryptionKey);
}

function isUserId(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(
    value,
  );
}

function isRepositoryId(value: string): boolean {
  return /^[1-9][0-9]{0,19}$/.test(value);
}

function isBranch(value: string): boolean {
  return value.length > 0
    && value.length <= 255
    && !value.startsWith("-")
    && !/[\u0000-\u001f\u007f]/.test(value);
}

function isCallbackUrl(value: string): boolean {
  try {
    const url = new URL(value);
    return (url.protocol === "https:" || url.protocol === "http:")
      && Boolean(url.hostname)
      && !url.username
      && !url.password
      && !url.search
      && !url.hash;
  } catch {
    return false;
  }
}

function isOAuthValue(value: string): boolean {
  return value.length >= 16
    && value.length <= 512
    && !/[\u0000-\u001f\u007f]/.test(value);
}

function githubFailure<T>(message: string, error: unknown): GitHubOutcome<T> {
  if (error instanceof GitHubApiError) {
    if (error.status === 401) {
      return { ok: false, code: "not_connected" };
    }
    if (error.status === 403) {
      return { ok: false, code: "access_denied" };
    }
    if (error.status === 404) {
      return { ok: false, code: "not_found" };
    }
    if (error.status === 409 || error.status === 422) {
      return { ok: false, code: "conflict" };
    }
    if (error.status === 429 || error.status >= 500) {
      return { ok: false, code: "temporarily_unavailable" };
    }
    if (error.status === 400) {
      return { ok: false, code: "access_denied" };
    }
  }
  return internalFailure(message, error);
}

function internalFailure<T>(message: string, error: unknown): GitHubOutcome<T> {
  console.error(message, error instanceof Error ? error.message : "Unknown error");
  return { ok: false, code: "internal_error" };
}

function isUniqueGitHubUserError(error: unknown): boolean {
  return error instanceof Error
    && error.message.includes(
      "UNIQUE constraint failed: github_connections.github_user_id",
    );
}
