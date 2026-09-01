import { afterEach, beforeEach, expect, test } from "bun:test"
import { authenticatedRequest } from "../src/authentication/internal/api"

const originalWindow = Object.getOwnPropertyDescriptor(globalThis, "window")
const originalFetch = globalThis.fetch

beforeEach(() => {
  Object.defineProperty(globalThis, "window", {
    configurable: true,
    value: { location: { origin: "https://app.example" } },
  })
})

afterEach(() => {
  globalThis.fetch = originalFetch
  if (originalWindow) {
    Object.defineProperty(globalThis, "window", originalWindow)
  } else {
    Reflect.deleteProperty(globalThis, "window")
  }
})

test("authenticated requests reject paths that URL parsing sends cross-origin", async () => {
  let requested = false
  globalThis.fetch = (() => {
    requested = true
    return Promise.resolve(new Response(null, { status: 204 }))
  }) as typeof fetch

  await expect(
    authenticatedRequest("secret", "/\\attacker.example/resource")
  ).rejects.toThrow("Authenticated requests require a same-origin path")
  expect(requested).toBe(false)
})

test("authenticated requests resolve and authorize same-origin paths", async () => {
  let requestedUrl: string | null = null
  let authorization: string | null = null
  globalThis.fetch = ((input, init) => {
    requestedUrl = String(input)
    authorization = new Headers(init?.headers).get("Authorization")
    return Promise.resolve(new Response(null, { status: 204 }))
  }) as typeof fetch

  await authenticatedRequest("secret", "/workspaces")

  expect(requestedUrl).toBe("https://app.example/workspaces")
  expect(authorization).toBe("Bearer secret")
})
