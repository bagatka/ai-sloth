/** The whole platform: Code + Sandboxes + Agents + Sessions. */

declare const brand: unique symbol;
type Id<Name extends string> = string & { readonly [brand]: Name };

export type UserId = Id<"user">;
export type WorkspaceId = Id<"workspace">;
export type SourceId = Id<"source">;
export type TargetId = Id<"target">;
export type VersionId = Id<"code-version">;
export type AgentId = Id<"agent">;
export type AgentStateId = Id<"agent-state">;
export type SessionId = Id<"session">;
export type TurnId = Id<"turn">;
export type PublicationId = Id<"publication">;

export interface Platform {
  readonly code: Code;
  readonly sandboxes: Sandboxes;
  readonly agents: Agents;
  readonly sessions: Sessions;
}

/** Owns immutable code versions and moves code into or out of the platform. */
export interface Code {
  import(user: UserId, source: SourceId): Promise<VersionId>;
  compare(user: UserId, before: VersionId, after: VersionId): Promise<string>;
  download(user: UserId, version: VersionId): Promise<ReadableStream<Uint8Array>>;

  publish(input: {
    user: UserId;
    version: VersionId;
    target: TargetId;
    message: string;
  }): Promise<{ id: PublicationId; url?: string }>;
}

/** A disposable computer. Cloudflare Sandbox and Docker are implementations. */
export interface Sandboxes {
  open(version: VersionId): Promise<Sandbox>;
}

export interface Sandbox {
  run(
    program: string,
    args?: readonly string[],
  ): Promise<{ exitCode: number; stdout: string; stderr: string }>;

  save(): Promise<VersionId>;
  close(): Promise<void>;
}

/** Turns one immutable code version into another. */
export interface Agents {
  run(input: {
    agent: AgentId;
    version: VersionId;
    message: string;
    state?: AgentStateId;
    emit?: (event: AgentEvent) => void | Promise<void>;
  }): Promise<{
    version: VersionId;
    reply: string;
    state?: AgentStateId;
  }>;
}

export type AgentEvent = Readonly<{
  type: "message" | "activity";
  text: string;
}>;

/** Owns collaboration, turn history, and the current code version. */
export interface Sessions {
  create(input: {
    user: UserId;
    workspace: WorkspaceId;
    version: VersionId;
    agent: AgentId;
  }): Promise<SessionId>;

  send(input: {
    user: UserId;
    session: SessionId;
    message: string;
  }): Promise<TurnId>;

  get(user: UserId, session: SessionId): Promise<Session>;
  watch(user: UserId, turn: TurnId, after?: number): AsyncIterable<SessionEvent>;
}

export type Session = Readonly<{
  id: SessionId;
  workspace: WorkspaceId;
  agent: AgentId;
  version: VersionId;
  status: "idle" | "running" | "failed";
  turns: readonly Turn[];
}>;

export type Turn = Readonly<{
  id: TurnId;
  author: UserId;
  status: "running" | "succeeded" | "failed";
  version?: VersionId;
}>;

export type SessionEvent =
  | Readonly<{ type: "user"; user: UserId; text: string }>
  | Readonly<{ type: "agent"; event: AgentEvent }>
  | Readonly<{ type: "done"; version: VersionId }>
  | Readonly<{ type: "failed"; message: string }>;
