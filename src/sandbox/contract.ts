export type AgentRunRequest = {
  repositoryUrl: string;
  branch: string;
  prompt: string;
};

export type AgentRunResponse = {
  output: string;
  truncated: boolean;
};

export function parseAgentRunRequest(value: unknown): AgentRunRequest | null {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    return null;
  }

  const { repositoryUrl, branch, prompt } = value as Record<string, unknown>;
  if (
    typeof repositoryUrl !== "string"
    || !repositoryUrl.trim()
    || typeof branch !== "string"
    || !branch.trim()
    || typeof prompt !== "string"
    || !prompt.trim()
  ) {
    return null;
  }

  return { repositoryUrl, branch, prompt };
}
