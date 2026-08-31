import { expect, test } from "bun:test";
import type { PiEvent } from "@ai-sloth/pi";
import { WorkingDiffTracker, type WorkingDiffUpdate } from "./working-diff";

function started(
  toolName: string,
  toolCallId = crypto.randomUUID(),
): Extract<PiEvent, { type: "tool_started" }> {
  return { type: "tool_started", toolCallId, toolName, input: {} };
}

function finished(toolName: string, toolCallId: string): PiEvent {
  return { type: "tool_finished", toolCallId, toolName, isError: false };
}

test("working diff snapshots are cached until a mutating tool runs", async () => {
  let captures = 0;
  const tracker = new WorkingDiffTracker(async () => ({
    status: "ready",
    patch: `patch-${++captures}`,
  }));
  const edit = started("edit", "edit-1");

  tracker.accept(edit);
  expect(await tracker.read()).toEqual({ status: "unavailable" });
  tracker.accept(finished("edit", edit.toolCallId));
  expect(await tracker.read()).toEqual({ status: "ready", patch: "patch-1" });
  expect(await tracker.read()).toEqual({ status: "ready", patch: "patch-1" });

  const read = started("read", "read-1");
  tracker.accept(read);
  tracker.accept(finished("read", read.toolCallId));
  expect(await tracker.read()).toEqual({ status: "ready", patch: "patch-1" });

  const shell = started("bash", "bash-1");
  tracker.accept(shell);
  tracker.accept(finished("bash", shell.toolCallId));
  expect(await tracker.read()).toEqual({ status: "ready", patch: "patch-2" });
  expect(captures).toBe(2);
});

test("an unavailable snapshot can be retried while the turn is active", async () => {
  let captures = 0;
  const tracker = new WorkingDiffTracker(async () =>
    ++captures === 1
      ? { status: "unavailable" }
      : { status: "ready", patch: "retried" },
  );

  expect(await tracker.read()).toEqual({ status: "unavailable" });
  expect(await tracker.read()).toEqual({ status: "ready", patch: "retried" });
  expect(captures).toBe(2);
});

test("closing ensures and retains one final aggregate snapshot", async () => {
  let captures = 0;
  const tracker = new WorkingDiffTracker(async () => ({
    status: "ready",
    patch: `patch-${++captures}`,
  }));
  const write = started("write", "write-1");
  tracker.accept(write);

  await tracker.close();

  expect(captures).toBe(1);
  expect(await tracker.read()).toEqual({ status: "ready", patch: "patch-1" });
});

test("a mutation invalidates a snapshot captured concurrently", async () => {
  let captures = 0;
  let resolveFirst: ((update: WorkingDiffUpdate) => void) | undefined;
  const tracker = new WorkingDiffTracker(async () => {
    captures += 1;
    if (captures === 1) {
      return new Promise((accept) => {
        resolveFirst = accept;
      });
    }
    return { status: "ready", patch: "fresh" };
  });

  const stale = tracker.read();
  const edit = started("edit", "edit-concurrent");
  tracker.accept(edit);
  resolveFirst?.({ status: "ready", patch: "stale" });

  expect(await stale).toEqual({ status: "unavailable" });
  tracker.accept(finished("edit", edit.toolCallId));
  expect(await tracker.read()).toEqual({ status: "ready", patch: "fresh" });
});

test("concurrent readers share one aggregate snapshot", async () => {
  let captures = 0;
  let resolve: ((update: WorkingDiffUpdate) => void) | undefined;
  const tracker = new WorkingDiffTracker(
    () =>
      new Promise((accept) => {
        captures += 1;
        resolve = accept;
      }),
  );

  const first = tracker.read();
  const second = tracker.read();
  resolve?.({ status: "ready", patch: "shared" });

  expect(await first).toEqual({ status: "ready", patch: "shared" });
  expect(await second).toEqual({ status: "ready", patch: "shared" });
  expect(captures).toBe(1);
});
