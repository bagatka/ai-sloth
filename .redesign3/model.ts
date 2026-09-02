/**
 * The complete coding-product surface for the task-first design.
 *
 * Authentication turns a request into an Actor before it reaches this API.
 * HTTP, Cloudflare, Git, GitHub credentials, Pi state, and task-image IDs are
 * deliberately absent.
 */

declare const brand: unique symbol;
type Id<Name extends string> = string & { readonly [brand]: Name };

export type UserId = Id<"user">;
export type TeamId = Id<"team">;
export type TaskId = Id<"task">;
export type TurnId = Id<"turn">;
export type UploadId = Id<"upload">;
export type PublicationId = Id<"publication">;

export type Actor = Readonly<{ userId: UserId }>;

/** Add source cases only when the corresponding create workflow exists. */
export type TaskSource =
  | Readonly<{
      kind: "github";
      repositoryId: string;
      branch: string;
    }>
  | Readonly<{
      kind: "upload";
      uploadId: UploadId;
    }>;

export interface Tasks {
  /** Returns only after the imported base is immutable and durably saved. */
  create(
    actor: Actor,
    input: {
      teamId: TeamId;
      name: string;
      source: TaskSource;
      agentProfile: string;
      instructions?: string;
      idempotencyKey: string;
    },
  ): Promise<Task>;

  /** Durably accepts one turn. A task with an active turn rejects another. */
  send(
    actor: Actor,
    input: {
      taskId: TaskId;
      message: string;
      idempotencyKey: string;
    },
  ): Promise<TurnAccepted>;

  get(actor: Actor, taskId: TaskId): Promise<Task>;

  events(
    actor: Actor,
    input: {
      taskId: TaskId;
      turnId: TurnId;
      after?: number;
      follow?: boolean;
      signal?: AbortSignal;
    },
  ): AsyncIterable<TaskEvent>;

  /** Cumulative, authoritative changes from the imported base to current code. */
  changes(actor: Actor, taskId: TaskId): Promise<Download>;

  /** A code-only archive; it never contains agent state or platform metadata. */
  download(actor: Actor, taskId: TaskId): Promise<Download>;

  /** The only initial external-write integration. */
  publishToGitHub(
    actor: Actor,
    input: {
      taskId: TaskId;
      repositoryId: string;
      baseBranch: string;
      message: string;
      idempotencyKey: string;
    },
  ): Promise<GitHubPublication>;

  remove(actor: Actor, taskId: TaskId): Promise<void>;
}

export type Task = Readonly<{
  id: TaskId;
  teamId: TeamId;
  name: string;
  createdBy: UserId;
  source: TaskSourceSummary;
  agentProfile: string;
  instructions?: string;
  status: "idle" | "running";
  turns: readonly Turn[];
  createdAt: string;
  updatedAt: string;
}>;

/** Stored source identity is immutable and contains no credential or clone URL. */
export type TaskSourceSummary =
  | Readonly<{
      kind: "github";
      repositoryId: string;
      commit: string;
      label: string;
    }>
  | Readonly<{
      kind: "upload";
      baseTree: string;
      label: string;
    }>;

export type TurnAccepted = Readonly<{
  taskId: TaskId;
  turnId: TurnId;
  status: "running";
}>;

export type Turn = Readonly<{
  id: TurnId;
  author: UserId;
  status: "running" | "completed" | "failed";
  /** Failed agent work can still be safely present in the current task. */
  changesSaved?: boolean;
  failure?: "agent" | "timeout" | "state_not_saved" | "interrupted";
  createdAt: string;
  completedAt?: string;
}>;

export type TaskEvent = Readonly<{
  sequence: number;
  occurredAt: string;
}> &
  (
    | Readonly<{ type: "user"; userId: UserId; text: string }>
    | Readonly<{ type: "assistant"; text: string; append: boolean }>
    | Readonly<{
        type: "tool";
        tool: string;
        phase: "started" | "output" | "finished";
        text?: string;
        failed?: boolean;
      }>
    | Readonly<{
        type: "finished";
        outcome: "completed" | "failed";
        changesSaved: boolean;
      }>
  );

export type Download = Readonly<{
  mediaType: string;
  fileName: string;
  size: number;
  content: ReadableStream<Uint8Array>;
}>;

export type GitHubPublication = Readonly<{
  id: PublicationId;
  requestedBy: UserId;
  commit: string;
  branch: string;
  pullRequest: Readonly<{ number: number; url: string }>;
}>;
