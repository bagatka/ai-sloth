import type { GitHubOperations } from "./github";
import { createGitHubOperations } from "./internal/github";
import { D1GitHubRepositoryStore } from "./internal/store";

export interface GitHubBindings {
  GITHUB_DB: D1Database;
  GITHUB_CLIENT_ID?: string;
  GITHUB_CLIENT_SECRET?: string;
  GITHUB_TOKEN_ENCRYPTION_KEY?: string;
}

export function bindGitHub(bindings: GitHubBindings): GitHubOperations {
  return createGitHubOperations(
    new D1GitHubRepositoryStore(bindings.GITHUB_DB),
    {
      clientId: bindings.GITHUB_CLIENT_ID ?? "",
      clientSecret: bindings.GITHUB_CLIENT_SECRET ?? "",
      tokenEncryptionKey: bindings.GITHUB_TOKEN_ENCRYPTION_KEY ?? "",
    },
  );
}
