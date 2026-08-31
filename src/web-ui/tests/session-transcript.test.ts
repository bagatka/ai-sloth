import { expect, test } from "bun:test"
import type { SessionEvent } from "../src/session"
import { repositoryActivity } from "../src/session/internal/repository-activity"
import { reduceSessionEvent } from "../src/session/internal/transcript"

const turnId = "e47f6e35-b7f3-4c6f-91f6-93f0479ec15b"

function event(sequence: number): SessionEvent {
  return {
    version: 1,
    turnId,
    sequence,
    occurredAt: "2026-08-30T10:00:00.000Z",
    type: "user_message",
    text: `message ${sequence}`,
  }
}

test("repository activity tracks live aggregate refresh boundaries", () => {
  const started: SessionEvent = {
    version: 1,
    turnId,
    sequence: 1,
    occurredAt: "2026-08-30T10:00:00.000Z",
    type: "tool_started",
    toolCallId: "call-1",
    toolName: "edit",
    input: { path: "src/app.ts", editCount: 1 },
  }
  const finished: SessionEvent = {
    version: 1,
    turnId,
    sequence: 2,
    occurredAt: "2026-08-30T10:00:01.000Z",
    type: "tool_finished",
    toolCallId: "call-1",
    toolName: "edit",
    isError: false,
  }

  expect(repositoryActivity([started])).toEqual({
    sequence: null,
    updating: true,
    reveal: true,
  })
  expect(repositoryActivity([started, finished])).toEqual({
    sequence: 2,
    updating: false,
    reveal: true,
  })
})

test("session transcripts order replayed events and ignore duplicate sequences", () => {
  const initial = new Map()
  const withSecond = reduceSessionEvent(initial, event(2))
  const ordered = reduceSessionEvent(withSecond, event(1))
  const duplicate = reduceSessionEvent(ordered, event(2))

  expect(ordered.get(turnId)?.map(({ sequence }) => sequence)).toEqual([1, 2])
  expect(duplicate).toBe(ordered)
})
