import { beforeEach, expect, test } from "bun:test"
import type { AccountSession } from "../src/authentication/internal/api"
import {
  readAccountSession,
  storeAccountSession,
} from "../src/authentication/internal/session-storage"

const storage = new MemoryStorage()

beforeEach(() => {
  storage.clear()
  Object.defineProperty(globalThis, "sessionStorage", {
    configurable: true,
    value: storage,
  })
})

test("account sessions survive a reload in the same tab", () => {
  const session: AccountSession = {
    account: {
      id: "a47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
      email: "user@example.com",
    },
    token: `asl_session_${"a".repeat(43)}`,
    expiresAt: Date.now() + 60_000,
  }

  storeAccountSession(session)

  expect(readAccountSession()).toEqual(session)
})

test("expired account sessions are not restored", () => {
  storeAccountSession({
    account: {
      id: "a47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
      email: "user@example.com",
    },
    token: `asl_session_${"a".repeat(43)}`,
    expiresAt: Date.now() - 1,
  })

  expect(readAccountSession()).toBeNull()
  expect(storage.length).toBe(0)
})

class MemoryStorage implements Storage {
  private readonly values = new Map<string, string>()

  get length() {
    return this.values.size
  }

  clear() {
    this.values.clear()
  }

  getItem(key: string) {
    return this.values.get(key) ?? null
  }

  key(index: number) {
    return [...this.values.keys()][index] ?? null
  }

  removeItem(key: string) {
    this.values.delete(key)
  }

  setItem(key: string, value: string) {
    this.values.set(key, value)
  }
}
