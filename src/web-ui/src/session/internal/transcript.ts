import type { SessionEvent } from ".."

export type SessionTranscript = ReadonlyMap<string, readonly SessionEvent[]>

export function reduceSessionEvent(
  transcript: SessionTranscript,
  event: SessionEvent
): SessionTranscript {
  const existing = transcript.get(event.turnId) ?? []
  if (existing.some(({ sequence }) => sequence === event.sequence)) {
    return transcript
  }

  const updated = new Map(transcript)
  updated.set(
    event.turnId,
    [...existing, event].sort((left, right) => left.sequence - right.sequence)
  )
  return updated
}
