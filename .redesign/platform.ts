/**
 * Strategic platform contracts.
 *
 * This file is intentionally provider-neutral and intentionally incomplete at
 * the product edge. It describes the seams justified by the current system and
 * known direction; it is not a plugin SDK or a migration target to land in one
 * change.
 */

// IDs are opaque strings at boundaries. Validation and generation have one
// owner; callers must not infer a provider or storage layout from an ID.
export type UserId = string;
export type WorkspaceId = string;
export type CodebaseId = string;
export type ProjectId = string;
export type SessionId = string;
export type TurnId = string;
export type ConnectorId = string;
export type ArtifactId = string;
export type UtcTimestamp = string;

export type Actor = Readonly<{
  userId: UserId;
}>;

export type WorkspaceActor = Readonly<{
  userId: UserId;
  workspaceId: WorkspaceId;
}>;

/** A workspace-owned product identity for a body of code. */
export type Codebase = Readonly<{
  id: CodebaseId;
  workspaceId: WorkspaceId;
  name: string;
  source: SourceSelection;
  defaultPublicationTarget?: PublicationTarget;
}>;

export type Project = Readonly<{
  id: ProjectId;
  codebaseId: CodebaseId;
  parentId: ProjectId | null;
  name: string;
  instructions: string;
  version: number;
}>;

/** A user-selected location. `revision` may name a mutable branch or object. */
export type SourceSelection = Readonly<{
  connector: ConnectorId;
  resource: string;
  revision?: string;
}>;

/** The immutable source identity recorded by a session. */
export type ResolvedSource = Readonly<{
  connector: ConnectorId;
  resource: string;
  version: string;
  label: string;
}>;

/**
 * Consumer-owned source capability.
 *
 * Implementations contain provider authentication, token refresh, URL/API
 * details, and exact-version validation. They never return credentials.
 */
export interface CodeSource {
  resolve(
    actor: WorkspaceActor,
    selection: SourceSelection,
  ): Promise<ResolvedSource>;

  /** Materialize exactly `source.version` into an empty workspace. */
  materialize(
    actor: WorkspaceActor,
    source: ResolvedSource,
    environment: ExecutionEnvironment,
    signal: AbortSignal,
  ): Promise<void>;
}

export type BinaryObject = Readonly<{
  content: ReadableStream<Uint8Array>;
  size: number;
  mediaType: string;
}>;

/** Immutable durable bytes. Authorization is never based on knowing this ID. */
export type ArtifactRef = Readonly<{
  id: ArtifactId;
  size: number;
  sha256: string;
  mediaType: string;
}>;

/**
 * Low-level immutable byte storage used behind domain modules.
 * Writes verify length/digest; reads verify stored metadata; deletes are
 * idempotent. The scope exists for retention and cleanup, not authorization.
 */
export interface ArtifactStore {
  write(
    scope: { workspaceId: WorkspaceId; sessionId: SessionId },
    value: BinaryObject,
  ): Promise<ArtifactRef>;

  read(reference: ArtifactRef): Promise<BinaryObject>;
  delete(references: readonly ArtifactRef[]): Promise<void>;
}

export type Command = Readonly<{
  executable: string;
  arguments?: readonly string[];
  cwd?: string;
  environment?: Readonly<Record<string, string>>;
  identity: "platform" | "agent";
  timeoutMs: number;
  outputLimitBytes: number;
}>;

export type CommandChunk = Readonly<{
  channel: "stdout" | "stderr";
  data: Uint8Array;
}>;

export type CommandResult = Readonly<{
  exitCode: number;
  timedOut: boolean;
  stdout: string;
  stderr: string;
  truncated: boolean;
}>;

export type EnvironmentFile = Readonly<{
  path: string;
  kind: "file" | "directory";
  size?: number;
}>;

export interface EnvironmentFiles {
  makeDirectory(path: string): Promise<void>;
  list(path: string): Promise<readonly EnvironmentFile[]>;
  read(path: string): Promise<BinaryObject>;
  write(
    path: string,
    content: string | Uint8Array | ReadableStream<Uint8Array>,
  ): Promise<void>;
}

/**
 * One disposable compute lease. It owns every process and temporary file it
 * creates. `destroy` is required on every exit and is safe to retry.
 */
export interface ExecutionEnvironment {
  readonly workspacePath: string;
  readonly scratchPath: string;
  readonly files: EnvironmentFiles;

  /** Output callbacks are awaited, providing backpressure to event journals. */
  run(
    command: Command,
    onOutput?: (chunk: CommandChunk) => void | Promise<void>,
    signal?: AbortSignal,
  ): Promise<CommandResult>;

  /** No agent-owned process may remain after this resolves. */
  stopAgentProcesses(): Promise<void>;
  destroy(): Promise<void>;
}

/** Cloudflare Sandbox, local Docker, or another disposable compute adapter. */
export interface ExecutionBackend {
  /** `profile` is a trusted server-side image/network/tool policy ID. */
  create(profile: string, signal: AbortSignal): Promise<ExecutionEnvironment>;
}

export type WorkspaceSnapshotRef = Readonly<{
  formatVersion: string;
  artifact: ArtifactRef;
}>;

export type ChangeSetRef = Readonly<{
  formatVersion: string;
  artifact: ArtifactRef;
}>;

/**
 * Authoritative workspace persistence. Capture is bounded and does not mutate
 * the workspace. Restore verifies integrity and accepts no external credential.
 */
export interface WorkspaceSnapshots {
  /** Bounded, provisional comparison; no durable revision is created. */
  inspectChanges(
    environment: ExecutionEnvironment,
    base: WorkspaceSnapshotRef,
    signal: AbortSignal,
  ): Promise<BinaryObject>;

  capture(
    environment: ExecutionEnvironment,
    input: { base?: WorkspaceSnapshotRef },
    signal: AbortSignal,
  ): Promise<{
    snapshot: WorkspaceSnapshotRef;
    changes: ChangeSetRef | null;
  }>;

  restore(
    environment: ExecutionEnvironment,
    snapshot: WorkspaceSnapshotRef,
    signal: AbortSignal,
  ): Promise<void>;
}

/** The state format is part of agent compatibility and is fixed per session. */
export type AgentKey = Readonly<{
  runner: string;
  version: string;
  profile: string;
}>;

export type AgentEvent =
  | { type: "assistant_started"; messageId: string }
  | { type: "assistant_delta"; messageId: string; text: string }
  | {
    type: "assistant_finished";
    messageId: string;
    reason: "complete" | "limit" | "cancelled" | "error";
  }
  | {
    type: "tool_started";
    toolCallId: string;
    toolName: string;
    mayWriteWorkspace: boolean;
    /** A bounded, redacted summary—not arbitrary native tool input. */
    input: Readonly<Record<string, string | number | boolean>>;
  }
  | { type: "tool_output"; toolCallId: string; text: string; append: boolean }
  | {
    type: "tool_finished";
    toolCallId: string;
    toolName: string;
    isError: boolean;
    /** Optional convenience only; workspace changes remain authoritative. */
    patch?: string;
    patchTruncated?: boolean;
  };

export interface AgentEventSink {
  /** Resolves only after the event is durably accepted. */
  append(event: AgentEvent): Promise<void>;
}

/**
 * Runs one selected agent and owns translation from its native protocol.
 * Process exits, native events, model credentials, and state bytes do not leak.
 */
export interface AgentRunner {
  run(input: {
    agent: AgentKey;
    environment: ExecutionEnvironment;
    prompt: string;
    instructions: string;
    previousState?: BinaryObject;
    events: AgentEventSink;
    signal: AbortSignal;
  }): Promise<{
    /** Optional for agents that do not have native continuation state. */
    state?: BinaryObject;
  }>;
}

export type SessionEventPayload =
  | { type: "user_message"; authorUserId: UserId; text: string }
  | AgentEvent
  | { type: "turn_failed"; code: string };

export type SessionEvent = Readonly<{
  version: 1;
  turnId: TurnId;
  sequence: number;
  occurredAt: UtcTimestamp;
}> & SessionEventPayload;

export type TranscriptRef = Readonly<{
  artifact: ArtifactRef;
  lastSequence: number;
}>;

/** Durable append-before-delivery event ordering and replay. */
export interface EventJournal {
  append(
    turnId: TurnId,
    events: readonly SessionEventPayload[],
  ): Promise<number>;

  seal(turnId: TurnId): Promise<TranscriptRef>;

  read(input: {
    turnId: TurnId;
    after: number;
    follow: boolean;
    signal: AbortSignal;
  }): AsyncIterable<SessionEvent>;
}

export type SessionBase = Readonly<{
  source: ResolvedSource;
  workspace: WorkspaceSnapshotRef;
}>;

export type Revision = Readonly<{
  number: number;
  turnId: TurnId;
  workspace: WorkspaceSnapshotRef;
  changes: ChangeSetRef | null;
  agentState: ArtifactRef | null;
  transcript: TranscriptRef;
  instructions: string;
  createdAt: UtcTimestamp;
}>;

export type TurnStatus =
  | "queued"
  | "running"
  | "finalizing"
  | "succeeded"
  | "failed"
  | "interrupted";

export type Turn = Readonly<{
  id: TurnId;
  sessionId: SessionId;
  authorUserId: UserId;
  ordinal: number;
  status: TurnStatus;
  failureCode: string | null;
  resultRevision: number | null;
  lastEventSequence: number;
  createdAt: UtcTimestamp;
  completedAt: UtcTimestamp | null;
}>;

export type Session = Readonly<{
  id: SessionId;
  workspaceId: WorkspaceId;
  codebaseId: CodebaseId;
  createdByUserId: UserId;
  name: string;
  projectId: ProjectId | null;
  agent: AgentKey;
  status: "running" | "idle" | "failed";
  source: ResolvedSource;
  /** Null only until the initial source tree has been captured successfully. */
  baseWorkspace: WorkspaceSnapshotRef | null;
  currentRevision: Revision | null;
  createdAt: UtcTimestamp;
  updatedAt: UtcTimestamp;
}>;

export type SessionView = Readonly<{
  id: SessionId;
  workspaceId: WorkspaceId;
  codebaseId: CodebaseId;
  createdByUserId: UserId;
  name: string;
  projectId: ProjectId | null;
  agent: AgentKey;
  status: Session["status"];
  sourceLabel: string;
  currentRevision: number | null;
  turns: readonly Turn[];
  publications: readonly PublicationReceipt[];
  createdAt: UtcTimestamp;
  updatedAt: UtcTimestamp;
}>;

export type AcceptedTurn = Readonly<{
  sessionId: SessionId;
  turnId: TurnId;
  status: "queued" | "running";
}>;

/** Input persisted atomically before a start request is acknowledged. */
export type StartTurnRecord = Readonly<{
  sessionId: SessionId;
  turnId: TurnId;
  idempotencyKey: string;
  workspaceId: WorkspaceId;
  codebaseId: CodebaseId;
  /** Resolved before durable acceptance so retries use the same source bytes. */
  source: ResolvedSource;
  createdByUserId: UserId;
  name: string;
  projectId: ProjectId | null;
  agent: AgentKey;
  prompt: string;
  instructions: string;
}>;

/** Input persisted atomically before a collaborative prompt is acknowledged. */
export type ContinueTurnRecord = Readonly<{
  turnId: TurnId;
  idempotencyKey: string;
  workspaceId: WorkspaceId;
  sessionId: SessionId;
  authorUserId: UserId;
  prompt: string;
  instructions: string;
}>;

export type TurnClaim = Readonly<{
  leaseId: string;
  leaseExpiresAt: UtcTimestamp;
  session: Session;
  turn: Turn;
  prompt: string;
  instructions: string;
  expectedRevision: number;
}>;

/**
 * Domain persistence, not a generic database wrapper. Acceptance is
 * idempotent. Claims are exclusive until expiry. Completion is an atomic CAS
 * from `expectedRevision` and is idempotent for the same lease/result.
 */
export interface SessionRepository {
  acceptStart(record: StartTurnRecord): Promise<AcceptedTurn>;
  acceptTurn(record: ContinueTurnRecord): Promise<AcceptedTurn>;

  claimTurn(input: {
    turnId: TurnId;
    workerId: string;
    leaseExpiresAt: UtcTimestamp;
  }): Promise<TurnClaim | null>;

  completeTurn(input: {
    claim: TurnClaim;
    base?: SessionBase;
    revision: Revision;
  }): Promise<void>;

  failTurn(input: {
    claim: TurnClaim;
    failureCode: string;
    transcript: TranscriptRef;
  }): Promise<void>;

  getSession(workspaceId: WorkspaceId, sessionId: SessionId): Promise<Session | null>;
  getTurn(workspaceId: WorkspaceId, turnId: TurnId): Promise<Turn | null>;
}

/**
 * Delivery is at least once; `SessionRepository.claimTurn` handles duplicates.
 * Acceptance also records a transactional outbox marker so enqueue failure
 * cannot strand a turn.
 */
export interface TurnQueue {
  enqueue(turnId: TurnId): Promise<void>;
}

/** A validated provider-owned target; `target` is opaque to session code. */
export type PublicationTarget = Readonly<{
  connector: ConnectorId;
  target: string;
}>;

export type PublicationReceipt = Readonly<{
  connector: ConnectorId;
  target: string;
  externalVersion: string;
  url?: string;
  publishedAt: UtcTimestamp;
}>;

/**
 * Explicit external write capability. A Git implementation may commit/push;
 * object storage may write a version. Credentials remain inside the adapter.
 */
export interface CodePublisher {
  publish(input: {
    actor: WorkspaceActor;
    target: PublicationTarget;
    source: ResolvedSource;
    base: WorkspaceSnapshotRef;
    revision: {
      number: number;
      workspace: WorkspaceSnapshotRef;
    };
    previousReceipt?: PublicationReceipt;
    summary: string;
    signal: AbortSignal;
  }): Promise<PublicationReceipt>;
}

/** The only membership capability needed by the session application. */
export interface WorkspaceAccess {
  requireMember(actor: Actor, workspaceId: WorkspaceId): Promise<WorkspaceActor>;
}

/** Transport-independent application surface. */
export interface SessionService {
  start(
    actor: Actor,
    input: {
      workspaceId: WorkspaceId;
      codebaseId: CodebaseId;
      sourceRevision?: string;
      agentProfile: string;
      projectId?: ProjectId;
      name: string;
      prompt: string;
      idempotencyKey: string;
    },
  ): Promise<AcceptedTurn>;

  send(
    actor: Actor,
    input: {
      workspaceId: WorkspaceId;
      sessionId: SessionId;
      prompt: string;
      idempotencyKey: string;
    },
  ): Promise<AcceptedTurn>;

  get(
    actor: Actor,
    workspaceId: WorkspaceId,
    sessionId: SessionId,
  ): Promise<SessionView>;

  events(
    actor: Actor,
    input: {
      workspaceId: WorkspaceId;
      sessionId: SessionId;
      turnId: TurnId;
      after: number;
      follow: boolean;
      signal: AbortSignal;
    },
  ): Promise<AsyncIterable<SessionEvent>>;

  changes(
    actor: Actor,
    input: {
      workspaceId: WorkspaceId;
      sessionId: SessionId;
      /** Present only when requesting a provisional active-turn comparison. */
      turnId?: TurnId;
    },
  ): Promise<BinaryObject>;

  publish(
    actor: Actor,
    input: {
      workspaceId: WorkspaceId;
      sessionId: SessionId;
      revision: number;
      target?: PublicationTarget;
      summary: string;
      idempotencyKey: string;
      signal: AbortSignal;
    },
  ): Promise<PublicationReceipt>;

  discard(
    actor: Actor,
    workspaceId: WorkspaceId,
    sessionId: SessionId,
  ): Promise<void>;
}
