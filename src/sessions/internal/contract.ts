import type { GitHubOperations } from "@ai-sloth/github";
import type { SandboxBindings } from "@ai-sloth/sandbox";

export type SessionResources = {
  sandbox: SandboxBindings["Sandbox"];
  database: D1Database;
  artifacts: R2Bucket;
  backupBucket: R2Bucket;
  github: GitHubOperations;
  imageVersion: string;
  serviceVersion: string;
  localBackups: boolean;
};

export type StartSessionInput = {
  sessionId: string;
  idempotencyKey: string;
  workspaceId: string;
  controllerUserId: string;
  name: string;
  projectId: string | null;
  githubRepositoryId: string;
  branch: string;
  prompt: string;
};

export type ContinueSessionInput = {
  sessionId: string;
  idempotencyKey: string;
  workspaceId: string;
  controllerUserId: string;
  prompt: string;
};

export type GetSessionInput = {
  sessionId: string;
  workspaceId: string;
  controllerUserId: string;
};

export type GetWorkingDiffInput = GetSessionInput & { turnId: string };

export type DiscardSessionInput = GetSessionInput;
export type PublishSessionInput = GetSessionInput;

export type SessionAccepted = {
  sessionId: string;
  turnId: string;
  status: SessionTurnStatus;
};

export type SessionDetails = {
  id: string;
  name: string;
  workspaceId: string;
  githubRepositoryId: string;
  projectId: string | null;
  status: SessionStatus;
  revision: number | null;
  publication: PublishSessionResult | null;
  createdAt: string;
  updatedAt: string;
  turns: SessionTurn[];
};

export type SessionDiff = {
  revision: number;
  size: number;
  content: ReadableStream<Uint8Array>;
};

export type SessionWorkingDiff = {
  turnId: string;
  size: number;
  content: ReadableStream<Uint8Array>;
};

export type SessionTurn = {
  id: string;
  ordinal: number;
  status: SessionTurnStatus;
  failureCode: string | null;
  resultRevision: number | null;
  lastEventSequence: number;
  createdAt: string;
  completedAt: string | null;
};

export type SessionStatus = "running" | "waiting" | "completed" | "failed";
export type SessionTurnStatus =
  | "running"
  | "finalizing"
  | "succeeded"
  | "failed"
  | "interrupted";

export type PublishSessionResult = {
  revision: number;
  commitSha: string;
  branch: string;
  pullRequest: { number: number; url: string };
};

export type SessionOutcome<T = SessionAccepted> =
  | { ok: true; value: T }
  | SessionFailure;

export type DiscardSessionOutcome = SessionOutcome<undefined>;
export type PublishSessionOutcome = SessionOutcome<PublishSessionResult>;
export type SessionDetailsOutcome = SessionOutcome<SessionDetails>;
export type SessionDiffOutcome = SessionOutcome<SessionDiff>;
export type SessionWorkingDiffOutcome = SessionOutcome<SessionWorkingDiff>;

export type SessionFailure = {
  ok: false;
  code: SessionFailureCode;
};

export type SessionFailureCode =
  | "not_found"
  | "not_controller"
  | "github_not_connected"
  | "repository_not_found"
  | "repository_access_denied"
  | "github_unavailable"
  | "project_not_found"
  | "project_instructions_too_large"
  | "revision_limit"
  | "turn_limit"
  | "session_limit"
  | "snapshot_too_large"
  | "checkpoint_too_large"
  | "transcript_too_large"
  | "diff_not_available"
  | "working_diff_not_available"
  | "conflict"
  | "checkout_timeout"
  | "checkout_failed"
  | "checkpoint_timeout"
  | "checkpoint_failed"
  | "publication_conflict"
  | "setup_timeout"
  | "setup_failed"
  | "agent_timeout"
  | "agent_failed"
  | "interrupted"
  | "internal_error";
