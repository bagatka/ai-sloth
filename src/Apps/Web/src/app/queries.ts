import { queryOptions } from "@tanstack/react-query"

import { answer, api } from "./api"

// What the screens read from the host, one definition per thing, so every screen shares one cached
// copy. Lists the host doesn't push yet are read again while the page is visible.
const listsRefresh = 10_000

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

export function chats(workspaceId: string) {
  return queryOptions({
    queryKey: ["workspaces", workspaceId, "chats"],
    queryFn: () =>
      answer(
        api.GET("/workspaces/{workspaceId}/chats", {
          params: { path: { workspaceId }, query: { limit: 100 } },
        })
      ),
    refetchInterval: listsRefresh,
  })
}

export function nooks(workspaceId: string) {
  return queryOptions({
    queryKey: ["workspaces", workspaceId, "nooks"],
    queryFn: () =>
      answer(
        api.GET("/workspaces/{workspaceId}/nooks", {
          params: { path: { workspaceId }, query: { limit: 200 } },
        })
      ),
    refetchInterval: listsRefresh,
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
