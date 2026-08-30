import { expect, test } from "bun:test";
import { TurnEventLog } from "./event-log";

test("journals normalized product events before replay", async () => {
  const storage = memoryStorage();
  const turnId = "e47f6e35-b7f3-4c6f-91f6-93f0479ec15b";
  const events = await TurnEventLog.create(storage, turnId);

  await events.appendUserMessage("Review this repository");
  events.acceptPiEvent({
    type: "assistant_message_started",
    messageId: "assistant-1",
  });
  events.acceptPiEvent({
    type: "assistant_block_delta",
    messageId: "assistant-1",
    contentIndex: 0,
    block: "thinking",
    text: "private reasoning",
  });
  events.acceptPiEvent({
    type: "assistant_block_delta",
    messageId: "assistant-1",
    contentIndex: 1,
    block: "text",
    text: "Found it",
  });

  const transcript = await events.finish();
  const stored = new TextDecoder().decode(transcript.content)
    .trimEnd().split("\n").map((line) => JSON.parse(line));

  expect(stored.map(({ type }) => type)).toEqual([
    "user_message",
    "assistant_message_started",
    "assistant_text_delta",
  ]);
  expect(JSON.stringify(stored)).not.toContain("private reasoning");
  expect(stored.map(({ sequence }) => sequence)).toEqual([1, 2, 3]);

  const response = events.response(1, false);
  const replayed = (await response.text()).trimEnd().split("\n")
    .map((line) => JSON.parse(line));
  expect(replayed.map(({ sequence }) => sequence)).toEqual([2, 3]);
});

function memoryStorage(): DurableObjectStorage {
  const values = new Map<string, unknown>();
  return {
    async get(key: string | string[]) {
      if (Array.isArray(key)) {
        return new Map(key.filter((item) => values.has(item)).map((item) => [
          item,
          values.get(item),
        ]));
      }
      return values.get(key);
    },
    async put(key: string | Record<string, unknown>, value?: unknown) {
      if (typeof key === "string") {
        values.set(key, value);
      } else {
        for (const [entry, item] of Object.entries(key)) values.set(entry, item);
      }
    },
    async delete(key: string | string[]) {
      if (Array.isArray(key)) {
        let deleted = 0;
        for (const item of key) if (values.delete(item)) deleted += 1;
        return deleted;
      }
      return values.delete(key);
    },
    async list(options?: DurableObjectListOptions) {
      return new Map([...values.entries()]
        .filter(([key]) => !options?.prefix || key.startsWith(options.prefix))
        .sort(([left], [right]) => left.localeCompare(right)));
    },
  } as unknown as DurableObjectStorage;
}
