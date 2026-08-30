import type {
  GitHubConnection,
  GitHubPullRequest,
  GitHubRepository,
} from "../github";

const GITHUB_API_ORIGIN = "https://api.github.com";
const GITHUB_OAUTH_ORIGIN = "https://github.com";
const API_VERSION = "2022-11-28";
const REQUEST_TIMEOUT_MS = 15_000;
const MAX_INSTALLATIONS = 100;
const MAX_REPOSITORIES_PER_INSTALLATION = 10_000;

export type OAuthTokens = {
  accessToken: string;
  accessTokenExpiresAt: number;
  refreshToken: string;
  refreshTokenExpiresAt: number;
};

export class GitHubApiError extends Error {
  constructor(readonly status: number, message: string) {
    super(message);
  }
}

export function createAuthorizationUrl(input: {
  clientId: string;
  callbackUrl: string;
  state: string;
  codeChallenge: string;
}): string {
  const url = new URL("/login/oauth/authorize", GITHUB_OAUTH_ORIGIN);
  url.searchParams.set("client_id", input.clientId);
  url.searchParams.set("redirect_uri", input.callbackUrl);
  url.searchParams.set("state", input.state);
  url.searchParams.set("code_challenge", input.codeChallenge);
  url.searchParams.set("code_challenge_method", "S256");
  return url.toString();
}

export async function exchangeAuthorizationCode(input: {
  clientId: string;
  clientSecret: string;
  code: string;
  callbackUrl: string;
  codeVerifier: string;
}): Promise<OAuthTokens> {
  return exchangeToken(new URLSearchParams({
    client_id: input.clientId,
    client_secret: input.clientSecret,
    code: input.code,
    redirect_uri: input.callbackUrl,
    code_verifier: input.codeVerifier,
  }));
}

export async function refreshAccessToken(input: {
  clientId: string;
  clientSecret: string;
  refreshToken: string;
}): Promise<OAuthTokens> {
  return exchangeToken(new URLSearchParams({
    client_id: input.clientId,
    client_secret: input.clientSecret,
    grant_type: "refresh_token",
    refresh_token: input.refreshToken,
  }));
}

export async function getCurrentUser(
  accessToken: string,
): Promise<GitHubConnection> {
  const value = await githubJson("/user", accessToken);
  if (!isObject(value) || !isSafeId(value.id) || !isLogin(value.login)) {
    throw new GitHubApiError(502, "GitHub returned an invalid user");
  }
  return { githubUserId: String(value.id), login: value.login };
}

export async function listInstallations(
  accessToken: string,
): Promise<string[]> {
  const query = new URLSearchParams({ per_page: String(MAX_INSTALLATIONS) });
  const value = await githubJson(`/user/installations?${query}`, accessToken);
  if (
    !isObject(value)
    || typeof value.total_count !== "number"
    || !Number.isSafeInteger(value.total_count)
    || value.total_count < 0
    || value.total_count > MAX_INSTALLATIONS
    || !Array.isArray(value.installations)
  ) {
    throw new GitHubApiError(502, "GitHub returned invalid installations");
  }
  return value.installations.map((installation) => {
    if (!isObject(installation) || !isSafeId(installation.id)) {
      throw new GitHubApiError(502, "GitHub returned an invalid installation");
    }
    return String(installation.id);
  });
}

export async function listInstallationRepositories(
  accessToken: string,
  installationId: string,
  page: number,
  perPage: number,
): Promise<{ repositories: GitHubRepository[]; totalCount: number }> {
  const query = new URLSearchParams({
    page: String(page),
    per_page: String(perPage),
  });
  const value = await githubJson(
    `/user/installations/${installationId}/repositories?${query}`,
    accessToken,
  );
  if (
    !isObject(value)
    || typeof value.total_count !== "number"
    || !Number.isSafeInteger(value.total_count)
    || value.total_count < 0
    || value.total_count > MAX_REPOSITORIES_PER_INSTALLATION
    || !Array.isArray(value.repositories)
  ) {
    throw new GitHubApiError(502, "GitHub returned invalid repositories");
  }
  return {
    repositories: value.repositories.map((repository) =>
      parseRepository(repository, installationId)
    ),
    totalCount: value.total_count,
  };
}

export async function getRepository(
  accessToken: string,
  repositoryId: string,
): Promise<GitHubRepository> {
  const value = await githubJson(`/repositories/${repositoryId}`, accessToken);
  return parseRepository(value, null);
}

export async function getBranchHead(
  accessToken: string,
  repository: GitHubRepository,
  branch: string,
): Promise<string | null> {
  try {
    const value = await githubJson(
      `/repos/${repository.fullName}/git/ref/heads/${encodeURIComponent(branch)}`,
      accessToken,
    );
    if (
      !isObject(value)
      || !isObject(value.object)
      || typeof value.object.sha !== "string"
      || !/^[0-9a-f]{40}$/.test(value.object.sha)
    ) {
      throw new GitHubApiError(502, "GitHub returned an invalid branch");
    }
    return value.object.sha;
  } catch (error) {
    if (error instanceof GitHubApiError && error.status === 404) return null;
    throw error;
  }
}

export async function createPullRequest(
  accessToken: string,
  repository: GitHubRepository,
  input: { head: string; base: string; title: string },
): Promise<GitHubPullRequest> {
  const query = new URLSearchParams({
    state: "open",
    head: `${repository.owner}:${input.head}`,
    base: input.base,
    per_page: "1",
  });
  const existing = await githubJson(
    `/repos/${repository.fullName}/pulls?${query}`,
    accessToken,
  );
  if (!Array.isArray(existing)) {
    throw new GitHubApiError(502, "GitHub returned invalid pull requests");
  }
  if (existing.length > 0) return parsePullRequest(existing[0]);

  const created = await githubJson(
    `/repos/${repository.fullName}/pulls`,
    accessToken,
    {
      method: "POST",
      body: JSON.stringify({
        title: input.title,
        head: input.head,
        base: input.base,
        draft: true,
      }),
    },
  );
  return parsePullRequest(created);
}

async function exchangeToken(body: URLSearchParams): Promise<OAuthTokens> {
  const value = await requestJson(
    new URL("/login/oauth/access_token", GITHUB_OAUTH_ORIGIN),
    {
      method: "POST",
      headers: {
        Accept: "application/json",
        "Content-Type": "application/x-www-form-urlencoded",
      },
      body,
    },
  );
  if (
    !isObject(value)
    || typeof value.access_token !== "string"
    || value.access_token.length === 0
    || typeof value.expires_in !== "number"
    || !Number.isSafeInteger(value.expires_in)
    || value.expires_in <= 0
    || typeof value.refresh_token !== "string"
    || value.refresh_token.length === 0
    || typeof value.refresh_token_expires_in !== "number"
    || !Number.isSafeInteger(value.refresh_token_expires_in)
    || value.refresh_token_expires_in <= 0
  ) {
    const error = isObject(value) && typeof value.error === "string"
      ? value.error
      : "invalid_response";
    throw new GitHubApiError(400, `GitHub OAuth failed: ${error}`);
  }
  const now = Date.now();
  return {
    accessToken: value.access_token,
    accessTokenExpiresAt: now + value.expires_in * 1000,
    refreshToken: value.refresh_token,
    refreshTokenExpiresAt: now + value.refresh_token_expires_in * 1000,
  };
}

async function githubJson(
  path: string,
  accessToken: string,
  init: RequestInit = {},
): Promise<unknown> {
  if (!path.startsWith("/") || path.startsWith("//")) {
    throw new Error("GitHub API paths must be relative");
  }
  const headers = new Headers(init.headers);
  headers.set("Accept", "application/vnd.github+json");
  headers.set("Authorization", `Bearer ${accessToken}`);
  headers.set("User-Agent", "ai-sloth");
  headers.set("X-GitHub-Api-Version", API_VERSION);
  if (init.body) headers.set("Content-Type", "application/json");
  return requestJson(new URL(path, GITHUB_API_ORIGIN), {
    ...init,
    headers,
  });
}

async function requestJson(url: URL, init: RequestInit): Promise<unknown> {
  const signal = AbortSignal.timeout(REQUEST_TIMEOUT_MS);
  let response: Response;
  try {
    response = await fetch(url, { ...init, signal });
  } catch (error) {
    throw new GitHubApiError(
      503,
      error instanceof Error ? error.message : "GitHub request failed",
    );
  }

  let value: unknown;
  try {
    value = await response.json();
  } catch {
    throw new GitHubApiError(response.status, "GitHub returned invalid JSON");
  }
  if (!response.ok) {
    throw new GitHubApiError(response.status, "GitHub request failed");
  }
  return value;
}

function parsePullRequest(value: unknown): GitHubPullRequest {
  if (
    !isObject(value)
    || typeof value.number !== "number"
    || !Number.isSafeInteger(value.number)
    || value.number <= 0
    || typeof value.html_url !== "string"
  ) {
    throw new GitHubApiError(502, "GitHub returned an invalid pull request");
  }
  const url = new URL(value.html_url);
  if (url.protocol !== "https:" || url.hostname !== "github.com") {
    throw new GitHubApiError(502, "GitHub returned an invalid pull request URL");
  }
  return { number: value.number, url: url.toString() };
}

function parseRepository(
  value: unknown,
  installationId: string | null,
): GitHubRepository {
  if (
    !isObject(value)
    || !isSafeId(value.id)
    || typeof value.name !== "string"
    || !isObject(value.owner)
    || !isLogin(value.owner.login)
    || typeof value.full_name !== "string"
    || value.full_name !== `${value.owner.login}/${value.name}`
    || typeof value.default_branch !== "string"
    || value.default_branch.length === 0
    || typeof value.private !== "boolean"
  ) {
    throw new GitHubApiError(502, "GitHub returned an invalid repository");
  }
  const permissions = isObject(value.permissions) ? value.permissions : null;
  return {
    id: String(value.id),
    installationId,
    name: value.name,
    owner: value.owner.login,
    fullName: value.full_name,
    defaultBranch: value.default_branch,
    private: value.private,
    canPush: permissions?.push === true,
  };
}

function isSafeId(value: unknown): value is number {
  return typeof value === "number" && Number.isSafeInteger(value) && value > 0;
}

function isLogin(value: unknown): value is string {
  return typeof value === "string"
    && value.length > 0
    && value.length <= 100
    && /^[A-Za-z0-9-]+$/.test(value);
}

function isObject(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value);
}
