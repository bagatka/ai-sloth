import { expect, test } from "bun:test"
import type { AuthenticatedRequest } from "../src/authentication"
import { createRepositoryCatalog } from "../src/repository-navigation"

const signal = new AbortController().signal

test("repository catalog persists nested projects and sessions", async () => {
  const requests: Array<{ path: string; init?: RequestInit }> = []
  const request = (async (path: string, init?: RequestInit) => {
    requests.push({ path, init })
    if (init?.method === "POST") {
      return Response.json(
        {
          sessionId: crypto.randomUUID(),
          turnId: crypto.randomUUID(),
          status: "running",
        },
        { status: 201 }
      )
    }
    return Response.json({
      items: [{ kind: "project", id: crypto.randomUUID(), name: "Nested" }],
      previousCursor: null,
      nextCursor: null,
    })
  }) as AuthenticatedRequest
  const catalog = createRepositoryCatalog(request)

  const page = await catalog.listProjectItems(
    "a47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
    "1296269",
    "b47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
    null,
    signal
  )
  expect(page.items[0]).toMatchObject({ kind: "project", name: "Nested" })
  expect(requests[0]?.path).toContain("projectId=b47f6e35")

  await catalog.createItem(
    "a47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
    "1296269",
    "b47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
    { kind: "session", name: "Review", branch: "main", prompt: "Review it" },
    signal
  )
  expect(requests[1]?.path).toBe(
    "/workspaces/a47f6e35-b7f3-4c6f-91f6-93f0479ec15b/sessions"
  )
  expect(
    new Headers(requests[1]?.init?.headers).get("Idempotency-Key")
  ).toMatch(/^[0-9a-f-]{36}$/)
  expect(JSON.parse(String(requests[1]?.init?.body))).toEqual({
    githubRepositoryId: "1296269",
    branch: "main",
    prompt: "Review it",
    name: "Review",
    projectId: "b47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
  })
})
