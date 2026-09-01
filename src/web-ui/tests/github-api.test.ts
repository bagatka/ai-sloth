import { expect, test } from "bun:test"
import { getGitHubConnection } from "../src/github/internal/api"

test("accepts a configured GitHub integration without a user connection", async () => {
  const request = async () =>
    Response.json({
      connection: null,
      installationUrl:
        "https://github.com/apps/ai-sloth-local/installations/new",
    })

  expect(
    await getGitHubConnection(request, new AbortController().signal)
  ).toEqual({
    connection: null,
    installationUrl: "https://github.com/apps/ai-sloth-local/installations/new",
  })
})
