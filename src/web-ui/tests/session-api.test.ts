import { expect, test } from "bun:test"
import type { AuthenticatedRequest } from "../src/authentication"
import { getSessionDiff, publishSession } from "../src/session/internal/api"

test("loads the complete session diff revision", async () => {
  let invocation: { path: string; init?: RequestInit } | undefined
  const patch = "diff --git a/file.txt b/file.txt\n"
  const request = (async (path: string, init?: RequestInit) => {
    invocation = { path, init }
    return new Response(patch, {
      headers: { "X-Session-Revision": "3" },
    })
  }) as AuthenticatedRequest

  const diff = await getSessionDiff(
    request,
    "a47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
    "b47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
    new AbortController().signal
  )

  expect(invocation?.path).toEndWith(
    "/sessions/b47f6e35-b7f3-4c6f-91f6-93f0479ec15b/diff"
  )
  expect(invocation?.init?.headers).toEqual({ Accept: "text/x-diff" })
  expect(diff).toEqual({ revision: 3, patch })
})

test("publishes the current session revision", async () => {
  let invocation: { path: string; init?: RequestInit } | undefined
  const request = (async (path: string, init?: RequestInit) => {
    invocation = { path, init }
    return Response.json({
      revision: 2,
      commitSha: "0123456789abcdef0123456789abcdef01234567",
      branch: "ai-sloth/session-id",
      pullRequest: {
        number: 42,
        url: "https://github.com/owner/repository/pull/42",
      },
    })
  }) as AuthenticatedRequest

  const publication = await publishSession(
    request,
    "a47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
    "b47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
    new AbortController().signal
  )

  expect(invocation?.path).toBe(
    "/workspaces/a47f6e35-b7f3-4c6f-91f6-93f0479ec15b/sessions/b47f6e35-b7f3-4c6f-91f6-93f0479ec15b/publish"
  )
  expect(invocation?.init?.method).toBe("POST")
  expect(publication.pullRequest.url).toContain("/pull/42")
})
