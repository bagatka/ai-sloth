import { bindGitHub, type GitHubBindings } from "@ai-sloth/github";
import type { SandboxBindings } from "@ai-sloth/sandbox";
import { DurableObject } from "cloudflare:workers";
import {
  discardSession,
  executePreparedTurn,
  failPreparedTurn,
  getSessionDetails,
  getSessionDiff,
  prepareContinuedSession,
  prepareStartSession,
  publishSession,
  sessionFailure,
  type PreparedSessionTurn,
} from "./internal/session";
import { TurnEventLog } from "./internal/event-log";
import type { WorkingDiffSource } from "./internal/working-diff";
import {
  TRUSTED_EVENT_USER_HEADER,
  TRUSTED_EVENT_WORKSPACE_HEADER,
} from "./internal/event-request";
import { SessionStore } from "./internal/session-store";
import type {
  ContinueSessionInput,
  DiscardSessionInput,
  DiscardSessionOutcome,
  GetSessionInput,
  GetWorkingDiffInput,
  PublishSessionInput,
  PublishSessionOutcome,
  SessionAccepted,
  SessionDetailsOutcome,
  SessionDiffOutcome,
  SessionFailure,
  SessionOutcome,
  SessionResources,
  SessionWorkingDiffOutcome,
  StartSessionInput,
} from "./internal/contract";

const MAX_EVENT_FOLLOWERS = 4;

export interface SessionBindings extends GitHubBindings, SandboxBindings {
  SESSION_DB: D1Database;
  SESSION_ARTIFACTS: R2Bucket;
  SANDBOX_IMAGE_VERSION?: string;
  CF_VERSION_METADATA?: WorkerVersionMetadata;
}

export type SessionCoordinatorNamespace =
  DurableObjectNamespace<SessionCoordinator>;

export class SessionCoordinator extends DurableObject<SessionBindings> {
  private active = false;
  private activeRequest:
    | { idempotencyKey: string; accepted: SessionAccepted }
    | undefined;
  private events: TurnEventLog | undefined;
  private retainedWorkingDiff: WorkingDiffState | undefined;
  private eventFollowers = 0;

  async start(input: StartSessionInput): Promise<SessionOutcome> {
    if (this.active) {
      return this.activeRequest?.idempotencyKey === input.idempotencyKey
        ? { ok: true, value: this.activeRequest.accepted }
        : { ok: false, code: "conflict" };
    }
    const prepared = await prepareStartSession(this.resources(), input);
    if (!prepared.ok || !prepared.value.attempt || !prepared.value.prompt) {
      return prepared.ok
        ? { ok: true, value: prepared.value.accepted }
        : prepared;
    }
    return this.launch(prepared.value);
  }

  async continue(input: ContinueSessionInput): Promise<SessionOutcome> {
    if (this.active) {
      return this.activeRequest?.idempotencyKey === input.idempotencyKey
        ? { ok: true, value: this.activeRequest.accepted }
        : { ok: false, code: "conflict" };
    }
    const prepared = await prepareContinuedSession(this.resources(), input);
    if (!prepared.ok || !prepared.value.attempt || !prepared.value.prompt) {
      return prepared.ok
        ? { ok: true, value: prepared.value.accepted }
        : prepared;
    }
    return this.launch(prepared.value);
  }

  get(input: GetSessionInput): Promise<SessionDetailsOutcome> {
    return getSessionDetails(this.resources(), input);
  }

  diff(input: GetSessionInput): Promise<SessionDiffOutcome> {
    return getSessionDiff(this.resources(), input);
  }

  async workingDiff(
    input: GetWorkingDiffInput,
  ): Promise<SessionWorkingDiffOutcome> {
    try {
      await new SessionStore(
        this.env.SESSION_DB,
        this.env.SESSION_ARTIFACTS,
      ).getTurn(
        input.sessionId,
        input.turnId,
        input.workspaceId,
        input.controllerUserId,
      );
    } catch (error) {
      return sessionFailure(error);
    }

    let current = this.retainedWorkingDiff;
    if (
      !current
      || current.sessionId !== input.sessionId
      || current.turnId !== input.turnId
    ) {
      return { ok: false, code: "working_diff_not_available" };
    }
    if (current.source) {
      const source = current.source;
      const update = await source.read();
      const latest = this.retainedWorkingDiff;
      if (!latest || latest.turnId !== input.turnId || latest.source !== source) {
        return { ok: false, code: "working_diff_not_available" };
      }
      this.retainedWorkingDiff = update.status === "ready"
        ? { ...latest, status: "ready", patch: update.patch }
        : { ...latest, status: "unavailable" };
      current = this.retainedWorkingDiff;
    }
    if (current.status !== "ready") {
      return { ok: false, code: "working_diff_not_available" };
    }

    const content = new Blob([current.patch]);
    return {
      ok: true,
      value: {
        turnId: current.turnId,
        size: content.size,
        content: content.stream(),
      },
    };
  }

  discard(input: DiscardSessionInput): Promise<DiscardSessionOutcome> {
    return this.runExclusive(() => discardSession(this.resources(), input));
  }

  publish(input: PublishSessionInput): Promise<PublishSessionOutcome> {
    return this.runExclusive(() => publishSession(this.resources(), input));
  }

  async fetch(request: Request): Promise<Response> {
    try {
      const identity = eventRequestIdentity(request);
      if (!identity) return jsonError("Invalid event request", 400);
      const sessions = new SessionStore(
        this.env.SESSION_DB,
        this.env.SESSION_ARTIFACTS,
      );
      const turn = await sessions.getTurn(
        identity.sessionId,
        identity.turnId,
        identity.workspaceId,
        identity.controllerUserId,
      );

      if (this.events?.turnId === turn.id) {
        if (identity.follow && this.eventFollowers >= MAX_EVENT_FOLLOWERS) {
          return jsonError("Too many event followers", 429);
        }
        if (identity.follow) this.eventFollowers += 1;
        return this.events.response(
          identity.after,
          identity.follow,
          request.signal,
          identity.follow
            ? () => {
              this.eventFollowers = Math.max(0, this.eventFollowers - 1);
            }
            : undefined,
        );
      }

      const retained = await TurnEventLog.restore(this.ctx.storage);
      if (retained?.turnId === turn.id) {
        if (retained.active && !this.active) {
          await retained.appendError("interrupted");
          const transcript = await retained.finish();
          await sessions.interrupt(identity.sessionId, turn, transcript);
          await retained.close();
        }
        return retained.response(identity.after, false, request.signal);
      }

      const transcript = await sessions.readTranscript(turn);
      if (!transcript) return emptyEventResponse();
      return archivedEventResponse(await transcript.text(), identity.after);
    } catch (error) {
      const failure = sessionFailure(error);
      return failure.code === "not_controller"
        ? jsonError("Forbidden", 403)
        : failure.code === "not_found"
        ? jsonError("Not found", 404)
        : jsonError("Could not load session events", 500);
    }
  }

  private async launch(prepared: PreparedSessionTurn): Promise<SessionOutcome> {
    if (this.active) return { ok: false, code: "conflict" };
    const attempt = prepared.attempt;
    const prompt = prepared.prompt;
    if (!attempt || !prompt) return { ok: false, code: "internal_error" };

    let events: TurnEventLog;
    try {
      events = await TurnEventLog.create(this.ctx.storage, attempt.turnId);
      await events.appendUserMessage(prompt);
    } catch (error) {
      const failed = await TurnEventLog.restore(this.ctx.storage);
      if (failed) {
        await failPreparedTurn(this.resources(), attempt, failed, error);
      } else {
        const failure = sessionFailure(error);
        try {
          await new SessionStore(
            this.env.SESSION_DB,
            this.env.SESSION_ARTIFACTS,
          ).fail(attempt, failure.code);
        } catch {
          console.error(
            "Session journal initialization failure could not be finalized",
          );
        }
      }
      return sessionFailure(error);
    }

    this.active = true;
    this.activeRequest = {
      idempotencyKey: attempt.idempotencyKey,
      accepted: prepared.accepted,
    };
    this.events = events;
    this.retainedWorkingDiff = {
      sessionId: attempt.id,
      turnId: attempt.turnId,
      status: "pending",
    };
    this.ctx.waitUntil(this.execute(prepared, events));
    return { ok: true, value: prepared.accepted };
  }

  private async execute(
    prepared: PreparedSessionTurn,
    events: TurnEventLog,
  ): Promise<void> {
    const attempt = prepared.attempt!;
    try {
      await executePreparedTurn(
        this.resources(),
        {
          attempt,
          prompt: prepared.prompt!,
          repository: prepared.repository,
        },
        events,
        (source) => {
          const current = this.retainedWorkingDiff;
          if (current?.turnId !== attempt.turnId) return;
          this.retainedWorkingDiff = { ...current, source };
        },
      );
    } catch (error) {
      await failPreparedTurn(this.resources(), attempt, events, error);
    } finally {
      try {
        await events.close();
      } catch {
        console.error("Session event journal close failed");
      }
      if (this.events === events) this.events = undefined;
      this.activeRequest = undefined;
      this.active = false;
    }
  }

  private async runExclusive<T>(
    operation: () => Promise<T>,
  ): Promise<T | SessionFailure> {
    if (this.active) return { ok: false, code: "conflict" };

    this.active = true;
    this.retainedWorkingDiff = undefined;
    try {
      return await operation();
    } finally {
      this.active = false;
    }
  }

  private resources(): SessionResources {
    return {
      sandbox: this.env.Sandbox,
      database: this.env.SESSION_DB,
      artifacts: this.env.SESSION_ARTIFACTS,
      backupBucket: this.env.BACKUP_BUCKET,
      github: bindGitHub(this.env),
      imageVersion: this.env.SANDBOX_IMAGE_VERSION ?? "1",
      serviceVersion: this.env.CF_VERSION_METADATA?.id ?? "local",
      localBackups: !(
        this.env.R2_ACCESS_KEY_ID
        && this.env.R2_SECRET_ACCESS_KEY
        && this.env.BACKUP_BUCKET_NAME
        && this.env.CLOUDFLARE_R2_ACCOUNT_ID
      ),
    };
  }
}

type WorkingDiffState = {
  sessionId: string;
  turnId: string;
  source?: WorkingDiffSource;
} & (
  | { status: "pending" | "unavailable" }
  | { status: "ready"; patch: string }
);

function eventRequestIdentity(request: Request): {
  sessionId: string;
  turnId: string;
  workspaceId: string;
  controllerUserId: string;
  after: number;
  follow: boolean;
} | null {
  const url = new URL(request.url);
  const match = url.pathname.match(
    /\/sessions\/([0-9a-f-]+)\/turns\/([0-9a-f-]+)\/events$/,
  );
  const after = Number(url.searchParams.get("after") ?? "0");
  const workspaceId = request.headers.get(TRUSTED_EVENT_WORKSPACE_HEADER) ?? "";
  const controllerUserId = request.headers.get(TRUSTED_EVENT_USER_HEADER) ?? "";
  return match
    && isId(match[1] ?? "")
    && isId(match[2] ?? "")
    && isId(workspaceId)
    && isId(controllerUserId)
    && Number.isSafeInteger(after)
    && after >= 0
    ? {
      sessionId: match[1]!,
      turnId: match[2]!,
      workspaceId,
      controllerUserId,
      after,
      follow: url.searchParams.get("follow") === "true",
    }
    : null;
}

function archivedEventResponse(ndjson: string, after: number): Response {
  const retained = ndjson.trimEnd().split("\n").filter((line) => {
    try {
      const value: unknown = JSON.parse(line);
      return isRecord(value)
        && Number.isSafeInteger(value.sequence)
        && Number(value.sequence) > after;
    } catch {
      throw new Error("Invalid archived session transcript");
    }
  });
  return new Response(retained.length > 0 ? `${retained.join("\n")}\n` : null, {
    headers: eventHeaders(),
  });
}

function emptyEventResponse(): Response {
  return new Response(null, { headers: eventHeaders() });
}

function eventHeaders(): HeadersInit {
  return {
    "Cache-Control": "no-store",
    "Content-Type": "application/x-ndjson; charset=utf-8",
    "X-Content-Type-Options": "nosniff",
  };
}

function jsonError(error: string, status: number): Response {
  return Response.json({ error }, { status });
}

function isId(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(
    value,
  );
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
