export type GitHubConnection = {
  githubUserId: string;
  login: string;
};

export type GitHubRepository = {
  id: string;
  installationId: string | null;
  name: string;
  owner: string;
  fullName: string;
  defaultBranch: string;
  private: boolean;
  canPush: boolean;
};

export type GitHubRepositoryPage = {
  items: GitHubRepository[];
  previousCursor: string | null;
  nextCursor: string | null;
};

export type GitHubRepositoryAccess = GitHubRepository & {
  githubUserId: string;
  cloneUrl: string;
  accessToken: string;
};

export type GitHubPullRequest = {
  number: number;
  url: string;
};

export type GitHubFailureCode =
  | "invalid_input"
  | "not_configured"
  | "not_connected"
  | "not_found"
  | "access_denied"
  | "conflict"
  | "temporarily_unavailable"
  | "internal_error";

export type GitHubOutcome<T> =
  | { ok: true; value: T }
  | { ok: false; code: GitHubFailureCode };

export type GitHubOperations = {
  startConnection(
    userId: string,
    callbackUrl: string,
  ): Promise<GitHubOutcome<{ authorizationUrl: string }>>;
  completeConnection(
    state: string,
    code: string,
  ): Promise<GitHubOutcome<GitHubConnection>>;
  getConnection(userId: string): Promise<GitHubOutcome<GitHubConnection>>;
  disconnect(userId: string): Promise<GitHubOutcome<undefined>>;
  listRepositories(
    userId: string,
    cursor: string | null,
  ): Promise<GitHubOutcome<GitHubRepositoryPage>>;
  getRepositoryAccess(
    userId: string,
    repositoryId: string,
    expectedGitHubUserId?: string,
  ): Promise<GitHubOutcome<GitHubRepositoryAccess>>;
  getBranchHead(
    userId: string,
    repositoryId: string,
    branch: string,
    expectedGitHubUserId: string,
  ): Promise<GitHubOutcome<string | null>>;
  createPullRequest(
    userId: string,
    repositoryId: string,
    input: {
      head: string;
      base: string;
      title: string;
      expectedGitHubUserId: string;
    },
  ): Promise<GitHubOutcome<GitHubPullRequest>>;
};
