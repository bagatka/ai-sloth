import type { PiEvent } from "@ai-sloth/pi";
import {
  SESSION_EVENT_VERSION,
  type SessionEvent,
  type SessionEventPayload,
} from "../events";

const EVENT_STATE_KEY = "session-events:state";
const EVENT_BATCH_PREFIX = "session-events:batch:";
const MAX_TRANSCRIPT_BYTES = 8 * 1024 * 1024;
const MAX_BATCH_BYTES = 32 * 1024;
const MAX_TEXT_BYTES = 32 * 1024;
const MAX_DIFF_BYTES = 64 * 1024;
const FLUSH_DELAY_MS = 50;
const FOLLOW_POLL_MS = 200;
const encoder = new TextEncoder();

export type TurnTranscript = {
  content: Uint8Array;
  size: number;
  lastSequence: number;
};

type EventState = {
  turnId: string;
  nextSequence: number;
  bytes: number;
  active: boolean;
};

export class TurnEventLog {
  readonly #storage: DurableObjectStorage;
  readonly #turnId: string;
  #state: EventState;
  #pending: SessionEventPayload[] = [];
  #pendingBytes = 0;
  #timer: ReturnType<typeof setTimeout> | undefined;
  #writes: Promise<void> = Promise.resolve();
  #failure: unknown;

  private constructor(storage: DurableObjectStorage, state: EventState) {
    this.#storage = storage;
    this.#turnId = state.turnId;
    this.#state = state;
  }

  static async create(
    storage: DurableObjectStorage,
    turnId: string,
  ): Promise<TurnEventLog> {
    await deleteEventState(storage);
    const state: EventState = {
      turnId,
      nextSequence: 1,
      bytes: 0,
      active: true,
    };
    await storage.put(EVENT_STATE_KEY, JSON.stringify(state));
    return new TurnEventLog(storage, state);
  }

  static async restore(
    storage: DurableObjectStorage,
  ): Promise<TurnEventLog | undefined> {
    const stored = await storage.get<string>(EVENT_STATE_KEY);
    if (!stored) return undefined;
    return new TurnEventLog(storage, parseState(stored));
  }

  get turnId(): string {
    return this.#turnId;
  }

  get active(): boolean {
    return this.#state.active;
  }

  async appendUserMessage(text: string): Promise<void> {
    this.#queue({ type: "user_message", text });
    await this.flush();
  }

  acceptPiEvent(event: PiEvent): void {
    if (this.#failure) throw this.#failure;
    for (const payload of normalizePiEvent(event)) this.#queue(payload);
  }

  async appendError(code: string): Promise<void> {
    this.#queue({ type: "agent_error", code });
    await this.flush();
  }

  async finish(): Promise<TurnTranscript> {
    await this.flush();
    await this.#writes;
    if (this.#failure) throw this.#failure;
    return this.snapshot();
  }

  async snapshot(): Promise<TurnTranscript> {
    const batches = await this.#storage.list<string>({
      prefix: EVENT_BATCH_PREFIX,
    });
    const content = encoder.encode([...batches.values()].join(""));
    if (content.byteLength !== this.#state.bytes) {
      throw new Error("Session event journal size did not match");
    }
    return {
      content,
      size: content.byteLength,
      lastSequence: this.#state.nextSequence - 1,
    };
  }

  async close(): Promise<void> {
    await this.flush();
    await this.#writes;
    this.#state = { ...this.#state, active: false };
    await this.#storage.put(EVENT_STATE_KEY, JSON.stringify(this.#state));
  }

  response(
    after: number,
    follow: boolean,
    signal?: AbortSignal,
    onClose?: () => void,
  ): Response {
    const storage = this.#storage;
    const turnId = this.#turnId;
    let cursor = after;
    let cancelled = false;
    let closed = false;
    const close = () => {
      if (closed) return;
      closed = true;
      onClose?.();
    };

    const content = new ReadableStream<Uint8Array>({
      async pull(controller) {
        while (!cancelled && !signal?.aborted) {
          const next = await readNextBatch(storage, turnId, cursor);
          if (next) {
            cursor = next.lastSequence;
            controller.enqueue(encoder.encode(next.ndjson));
            return;
          }

          const state = await readState(storage);
          if (!follow || !state || state.turnId !== turnId || !state.active) {
            controller.close();
            close();
            return;
          }
          await scheduler.wait(FOLLOW_POLL_MS);
        }
        controller.close();
        close();
      },
      cancel() {
        cancelled = true;
        close();
      },
    }, {
      highWaterMark: 64 * 1024,
      size: (chunk) => chunk.byteLength,
    });

    return new Response(content, {
      headers: {
        "Cache-Control": "no-store",
        "Content-Type": "application/x-ndjson; charset=utf-8",
        "X-Content-Type-Options": "nosniff",
      },
    });
  }

  #queue(payload: SessionEventPayload): void {
    if (!this.#state.active) throw new Error("Session event journal is closed");
    if (this.#failure) throw this.#failure;
    this.#pending.push(payload);
    this.#pendingBytes += encodedSize(payload);
    if (this.#pendingBytes >= MAX_BATCH_BYTES) {
      this.#scheduleWrite();
      return;
    }
    if (!this.#timer) {
      this.#timer = setTimeout(() => {
        this.#timer = undefined;
        this.#scheduleWrite();
      }, FLUSH_DELAY_MS);
    }
  }

  async flush(): Promise<void> {
    if (this.#timer) {
      clearTimeout(this.#timer);
      this.#timer = undefined;
    }
    this.#scheduleWrite();
    await this.#writes;
    if (this.#failure) throw this.#failure;
  }

  #scheduleWrite(): void {
    if (this.#pending.length === 0 || this.#failure) return;
    const payloads = this.#pending;
    this.#pending = [];
    this.#pendingBytes = 0;
    this.#writes = this.#writes.then(() => this.#persist(payloads));
    void this.#writes.catch((error) => {
      this.#failure = error;
    });
  }

  async #persist(payloads: SessionEventPayload[]): Promise<void> {
    const firstSequence = this.#state.nextSequence;
    const events = payloads.map((payload, index): SessionEvent => ({
      version: SESSION_EVENT_VERSION,
      turnId: this.#turnId,
      sequence: firstSequence + index,
      occurredAt: new Date().toISOString(),
      ...payload,
    }));
    const ndjson = `${events.map((event) => JSON.stringify(event)).join("\n")}\n`;
    const bytes = encoder.encode(ndjson).byteLength;
    if (this.#state.bytes + bytes > MAX_TRANSCRIPT_BYTES) {
      throw new EventLogError("transcript_too_large");
    }
    const state = {
      ...this.#state,
      nextSequence: firstSequence + events.length,
      bytes: this.#state.bytes + bytes,
    };
    await this.#storage.put({
      [batchKey(firstSequence)]: ndjson,
      [EVENT_STATE_KEY]: JSON.stringify(state),
    });
    this.#state = state;
  }
}

export class EventLogError extends Error {
  constructor(readonly code: "transcript_too_large") {
    super(code);
  }
}

async function deleteEventState(storage: DurableObjectStorage): Promise<void> {
  const batches = await storage.list({ prefix: EVENT_BATCH_PREFIX });
  await storage.delete([EVENT_STATE_KEY, ...batches.keys()]);
}

async function readState(
  storage: DurableObjectStorage,
): Promise<EventState | undefined> {
  const stored = await storage.get<string>(EVENT_STATE_KEY);
  return stored ? parseState(stored) : undefined;
}

function parseState(value: string): EventState {
  const parsed: unknown = JSON.parse(value);
  if (
    !isRecord(parsed)
    || typeof parsed.turnId !== "string"
    || !Number.isSafeInteger(parsed.nextSequence)
    || Number(parsed.nextSequence) < 1
    || !Number.isSafeInteger(parsed.bytes)
    || Number(parsed.bytes) < 0
    || typeof parsed.active !== "boolean"
  ) {
    throw new Error("Invalid session event journal state");
  }
  return parsed as EventState;
}

async function readNextBatch(
  storage: DurableObjectStorage,
  turnId: string,
  after: number,
): Promise<{ ndjson: string; lastSequence: number } | undefined> {
  const batches = await storage.list<string>({ prefix: EVENT_BATCH_PREFIX });
  for (const ndjson of batches.values()) {
    const events = ndjson.trimEnd().split("\n").map(parseEventIdentity);
    if (events.length === 0 || events[0]!.turnId !== turnId) continue;
    const retained = events.filter((event) => event.sequence > after);
    if (retained.length === 0) continue;
    return {
      ndjson: `${retained.map((event) => JSON.stringify(event)).join("\n")}\n`,
      lastSequence: retained.at(-1)!.sequence,
    };
  }
  return undefined;
}

function parseEventIdentity(line: string): SessionEvent {
  const value: unknown = JSON.parse(line);
  if (
    !isRecord(value)
    || value.version !== SESSION_EVENT_VERSION
    || typeof value.turnId !== "string"
    || !Number.isSafeInteger(value.sequence)
    || Number(value.sequence) < 1
    || typeof value.occurredAt !== "string"
    || typeof value.type !== "string"
  ) {
    throw new Error("Invalid stored session event");
  }
  return value as SessionEvent;
}

function normalizePiEvent(event: PiEvent): SessionEventPayload[] {
  switch (event.type) {
    case "assistant_message_started":
      return [{ ...event }];
    case "assistant_block_delta":
      return event.block === "text"
        ? splitText(event.text).map((text) => ({
          type: "assistant_text_delta" as const,
          messageId: event.messageId,
          contentIndex: event.contentIndex,
          text,
        }))
        : [];
    case "assistant_message_finished":
    case "tool_started":
    case "retry_started":
    case "retry_finished":
    case "compaction_started":
    case "compaction_finished":
      return [{ ...event }];
    case "tool_output":
      return splitText(event.text).map((text, index) => ({
        type: "tool_output" as const,
        toolCallId: event.toolCallId,
        text,
        append: index === 0 ? event.append : true,
      }));
    case "tool_finished": {
      const diff = event.diff === undefined
        ? undefined
        : truncateUtf8(event.diff, MAX_DIFF_BYTES);
      return [{
        ...event,
        ...(diff
          ? { diff: diff.text, diffTruncated: event.diffTruncated || diff.truncated }
          : {}),
      }];
    }
    case "agent_started":
    case "agent_settled":
    case "turn_started":
    case "turn_finished":
    case "assistant_block_started":
    case "assistant_block_finished":
      return [];
  }
}

function splitText(text: string): string[] {
  if (encoder.encode(text).byteLength <= MAX_TEXT_BYTES) return [text];
  const chunks: string[] = [];
  let remaining = text;
  while (remaining.length > 0) {
    const chunk = truncateUtf8(remaining, MAX_TEXT_BYTES).text;
    if (chunk.length === 0) break;
    chunks.push(chunk);
    remaining = remaining.slice(chunk.length);
  }
  return chunks;
}

function truncateUtf8(
  text: string,
  maximumBytes: number,
): { text: string; truncated: boolean } {
  const encoded = encoder.encode(text);
  if (encoded.byteLength <= maximumBytes) return { text, truncated: false };
  return {
    text: new TextDecoder().decode(encoded.subarray(0, maximumBytes)),
    truncated: true,
  };
}

function encodedSize(payload: SessionEventPayload): number {
  return encoder.encode(JSON.stringify(payload)).byteLength + 128;
}

function batchKey(firstSequence: number): string {
  return `${EVENT_BATCH_PREFIX}${firstSequence.toString().padStart(10, "0")}`;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
