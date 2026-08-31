import type { PiEvent } from "@ai-sloth/pi";

export type WorkingDiffUpdate =
  | { status: "ready"; patch: string }
  | { status: "unavailable" };

export type WorkingDiffSource = {
  read(): Promise<WorkingDiffUpdate>;
};

export class WorkingDiffTracker implements WorkingDiffSource {
  readonly #capture: () => Promise<WorkingDiffUpdate>;
  readonly #activeTools = new Set<string>();
  #version = 0;
  #cached: { version: number; update: WorkingDiffUpdate } | undefined;
  #inFlight:
    | { version: number; update: Promise<WorkingDiffUpdate> }
    | undefined;
  #closing: Promise<void> | undefined;
  #closed = false;

  constructor(capture: () => Promise<WorkingDiffUpdate>) {
    this.#capture = capture;
  }

  accept(event: PiEvent): void {
    if (this.#closed) return;
    if (event.type === "tool_started" && mutatesRepository(event.toolName)) {
      this.#activeTools.add(event.toolCallId);
      this.#version += 1;
      return;
    }
    if (event.type === "tool_finished" && mutatesRepository(event.toolName)) {
      if (!this.#activeTools.delete(event.toolCallId)) this.#version += 1;
    }
  }

  async read(): Promise<WorkingDiffUpdate> {
    if (this.#activeTools.size > 0) return { status: "unavailable" };
    if (this.#cached?.version === this.#version) return this.#cached.update;
    if (this.#closed) return { status: "unavailable" };
    if (this.#inFlight) {
      if (this.#inFlight.version === this.#version) {
        return this.#inFlight.update;
      }
      await this.#inFlight.update;
      return this.read();
    }

    const version = this.#version;
    const update = this.#captureCurrent(version);
    this.#inFlight = { version, update };
    try {
      return await update;
    } finally {
      if (this.#inFlight?.update === update) this.#inFlight = undefined;
    }
  }

  close(): Promise<void> {
    if (this.#closing) return this.#closing;
    this.#closing = (async () => {
      this.#activeTools.clear();
      await this.read();
      this.#closed = true;
    })();
    return this.#closing;
  }

  async #captureCurrent(version: number): Promise<WorkingDiffUpdate> {
    let update: WorkingDiffUpdate;
    try {
      update = await this.#capture();
    } catch {
      update = { status: "unavailable" };
    }
    if (version !== this.#version || this.#activeTools.size > 0) {
      return { status: "unavailable" };
    }
    if (update.status === "ready") this.#cached = { version, update };
    return update;
  }
}

function mutatesRepository(toolName: string): boolean {
  return toolName === "bash" || toolName === "edit" || toolName === "write";
}
