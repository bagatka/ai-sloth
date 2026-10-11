import { keepPreviousData, queryOptions } from "@tanstack/react-query"

import { ApiError, answer, api } from "./api"

// What the screens read from the host, one definition per thing, so every screen shares one cached
// copy. A workspace's chats come live instead (workspace-chats.ts).

export const discovery = queryOptions({
  queryKey: ["discovery"],
  queryFn: () => answer(api.GET("/.well-known/aisloth")),
  staleTime: Infinity,
})

export const me = queryOptions({
  queryKey: ["me"],
  queryFn: () => answer(api.GET("/users/me")),
  staleTime: 5 * 60_000,
})

export const workspaces = queryOptions({
  queryKey: ["workspaces"],
  queryFn: () =>
    answer(api.GET("/workspaces", { params: { query: { limit: 200 } } })),
  staleTime: 60_000,
})

export function nooks(workspaceId: string) {
  return queryOptions({
    queryKey: ["workspaces", workspaceId, "nooks"],
    queryFn: () =>
      answer(
        api.GET("/workspaces/{workspaceId}/nooks", {
          params: { path: { workspaceId }, query: { limit: 200 } },
        })
      ),
    // Read again whenever the workspace's chats change (router.tsx). Not handled: a nook falling
    // asleep or waking without its chat changing shows within half a minute, rather than at once;
    // it would take a live list of nooks.
    refetchInterval: 30_000,
  })
}

export function chat(chatId: string) {
  return queryOptions({
    queryKey: ["chats", chatId],
    queryFn: () =>
      answer(api.GET("/chats/{id}", { params: { path: { id: chatId } } })),
  })
}

/** People by their IDs, as others see them: their names. */
export function people(ids: readonly string[]) {
  const sorted = [...new Set(ids)].sort()
  return queryOptions({
    queryKey: ["people", ...sorted],
    queryFn: () =>
      answer(api.GET("/users", { params: { query: { ids: sorted } } })),
    staleTime: 10 * 60_000,
    enabled: sorted.length > 0,
  })
}

/** The harnesses this host runs agents with, for their names. */
export const harnesses = queryOptions({
  queryKey: ["harnesses"],
  queryFn: () => answer(api.GET("/harnesses")),
  staleTime: Infinity,
})

/** The agent accounts people in the workspace may run chats on: the workspace's and their own. */
export function agentAccounts(workspaceId: string) {
  return queryOptions({
    queryKey: ["workspaces", workspaceId, "agent-accounts"],
    queryFn: () =>
      answer(
        api.GET("/workspaces/{workspaceId}/agent-accounts", {
          params: { path: { workspaceId } },
        })
      ),
  })
}

/**
 * What the account's plan has used of its limits, as its vendor reports them: a ChatGPT plan's as its
 * latest call did, a Copilot or Claude plan's asked of the vendor; nothing for an API key.
 */
export function planUsage(workspaceId: string, account: string) {
  return queryOptions({
    queryKey: ["workspaces", workspaceId, "agent-accounts", account, "usage"],
    queryFn: () =>
      answer(
        api.GET("/workspaces/{workspaceId}/agent-accounts/{id}/usage", {
          params: { path: { workspaceId, id: account } },
        })
      ),
    staleTime: 60_000,
  })
}

/**
 * The models a chat's agent can run on with the harness and the account, newest first; empty when
 * the harness picks its own. The host reads the vendor's catalog each time, which changes rarely
 * within a session.
 */
export function models(workspaceId: string, harness: string, account: string) {
  return queryOptions({
    queryKey: ["workspaces", workspaceId, "models", harness, account],
    queryFn: () =>
      answer(
        api.GET("/workspaces/{workspaceId}/models", {
          params: { path: { workspaceId }, query: { harness, account } },
        })
      ),
    staleTime: 30 * 60_000,
  })
}

/** Your presets that can start chats in the workspace, oldest first. */
export function presets(workspaceId: string) {
  return queryOptions({
    queryKey: ["workspaces", workspaceId, "presets"],
    queryFn: () =>
      answer(
        api.GET("/workspaces/{workspaceId}/presets", {
          params: { path: { workspaceId } },
        })
      ),
  })
}

/** Where the workspace's nooks can run, and whether each can start one now. */
export function providers(workspaceId: string) {
  return queryOptions({
    queryKey: ["workspaces", workspaceId, "providers"],
    queryFn: () =>
      answer(
        api.GET("/workspaces/{workspaceId}/providers", {
          params: { path: { workspaceId } },
        })
      ),
  })
}

/** The repositories the workspace connected, which its chats can start with. */
export function repositories(workspaceId: string) {
  return queryOptions({
    queryKey: ["workspaces", workspaceId, "repositories"],
    queryFn: () =>
      answer(
        api.GET("/workspaces/{workspaceId}/repositories", {
          params: { path: { workspaceId } },
        })
      ),
  })
}

/**
 * What the nook's repositories changed: live from an awake nook, otherwise as its latest checkpoint
 * kept them. Asking never wakes a nook. Each checkpoint is read anew, the last reading showing until
 * the new one is in.
 */
export function changes(nookId: string, checkpoint: number) {
  return queryOptions({
    queryKey: ["nooks", nookId, "changes", checkpoint],
    placeholderData: keepPreviousData,
    queryFn: () =>
      answer(
        api.GET("/nooks/{id}/changes", { params: { path: { id: nookId } } })
      ),
    staleTime: 0,
  })
}

/**
 * What the chat's agents have used of their models: tokens and cost, in all and by turn, how full
 * the context is, and until when the prompt's cache stays. Read again at each checkpoint, which
 * every turn ends with, and as the caller asks while the agent works.
 */
export function usage(chatId: string, checkpoint: number) {
  return queryOptions({
    queryKey: ["chats", chatId, "usage", checkpoint],
    placeholderData: keepPreviousData,
    queryFn: () =>
      answer(
        api.GET("/chats/{id}/usage", { params: { path: { id: chatId } } })
      ),
    staleTime: 0,
  })
}

/**
 * What the workspace's chats used of their models over its last days, today included, by day in the
 * person's own time zone and by chat. Read again every minute while shown.
 */
export function workspaceUsage(workspaceId: string, days: number) {
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone
  return queryOptions({
    queryKey: ["workspaces", workspaceId, "usage", days, timeZone],
    queryFn: () =>
      answer(
        api.GET("/workspaces/{workspaceId}/usage", {
          params: { path: { workspaceId }, query: { days, timeZone } },
        })
      ),
    staleTime: 60_000,
    refetchInterval: 60_000,
  })
}

/** Every kind of agent account, how each is added, and whether this host allows it. */
export const accountKinds = queryOptions({
  queryKey: ["agent-account-kinds"],
  queryFn: () => answer(api.GET("/agent-account-kinds")),
  staleTime: Infinity,
})

/** The person's GitHub account, or null when they haven't connected one. */
export const gitHubAccount = queryOptions({
  queryKey: ["github", "account"],
  queryFn: () =>
    answer(api.GET("/github/account")).catch((error: unknown) => {
      if (
        error instanceof ApiError &&
        error.code === "sources.github_not_connected"
      ) {
        return null
      }
      throw error
    }),
})

/** The repositories the person's GitHub connection reaches, and where to install the app on more. */
export const availableRepositories = queryOptions({
  queryKey: ["github", "repositories"],
  queryFn: () => answer(api.GET("/github/repositories")),
})

export function secrets(workspaceId: string) {
  return queryOptions({
    queryKey: ["workspaces", workspaceId, "secrets"],
    queryFn: () =>
      answer(
        api.GET("/workspaces/{workspaceId}/secrets", {
          params: { path: { workspaceId } },
        })
      ),
  })
}

/** The workspace's instructions, and the person's own. */
export function instructions(workspaceId: string) {
  return queryOptions({
    queryKey: ["workspaces", workspaceId, "instructions"],
    queryFn: () =>
      answer(
        api.GET("/workspaces/{workspaceId}/instructions", {
          params: { path: { workspaceId } },
        })
      ),
  })
}

/** The people given access to the workspace. */
export function workspaceAccess(workspaceId: string) {
  return queryOptions({
    queryKey: ["workspaces", workspaceId, "access"],
    queryFn: () =>
      answer(
        api.GET("/workspaces/{workspaceId}/access", {
          params: { path: { workspaceId } },
        })
      ),
  })
}
