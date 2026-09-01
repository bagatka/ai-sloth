import { useCallback, useEffect, useRef, useState } from "react"
import type { Page, RepositoryItem, RepositoryCatalog } from ".."

type CollectionId = string | null
type LoadDirection = "initial" | "next" | "previous"

type CollectionState = {
  pages: Page<RepositoryItem>[]
  loading: boolean
  error: string | null
}

type FailedLoad = {
  cursor: string | null
  direction: LoadDirection
}

const EMPTY_COLLECTION: CollectionState = {
  pages: [],
  loading: true,
  error: null,
}

export function useRepositoryCollections(
  catalog: RepositoryCatalog,
  workspaceId: string,
  repositoryId: string,
  maximumItems: number
) {
  const states = useRef(new Map<CollectionId, CollectionState>())
  const activeCollections = useRef(new Set<CollectionId>())
  const activeRequests = useRef(new Map<CollectionId, AbortController>())
  const failedLoads = useRef(new Map<CollectionId, FailedLoad>())
  const mounted = useRef(false)
  const [collections, setCollections] = useState(states.current)

  const publish = useCallback(
    (collectionId: CollectionId, state: CollectionState) => {
      states.current.set(collectionId, state)
      if (mounted.current) setCollections(new Map(states.current))
    },
    []
  )

  const requestPage = useCallback(
    function requestPage(
      collectionId: CollectionId,
      cursor: string | null,
      direction: LoadDirection
    ) {
      if (activeRequests.current.has(collectionId)) return

      const controller = new AbortController()
      activeRequests.current.set(collectionId, controller)
      const current = states.current.get(collectionId) ?? EMPTY_COLLECTION
      publish(collectionId, { ...current, loading: true, error: null })

      const load = collectionId
        ? catalog.listProjectItems(
            workspaceId,
            repositoryId,
            collectionId,
            cursor,
            controller.signal
          )
        : catalog.listRepositoryItems(
            workspaceId,
            repositoryId,
            cursor,
            controller.signal
          )

      void load
        .then((page) => {
          if (activeRequests.current.get(collectionId) !== controller) return

          failedLoads.current.delete(collectionId)
          const existing = states.current.get(collectionId)?.pages ?? []
          const pages =
            direction === "initial"
              ? [page]
              : trimPages(
                  direction === "next"
                    ? [...existing, page]
                    : [page, ...existing],
                  direction,
                  maximumItems
                )
          publish(collectionId, { pages, loading: true, error: null })

          if (activeCollections.current.has(collectionId)) {
            for (const item of pages.flatMap(({ items }) => items)) {
              if (item.kind !== "project") continue
              const child = states.current.get(item.id)
              if (
                !activeRequests.current.has(item.id) &&
                !child?.pages.length &&
                !child?.error
              ) {
                requestPage(item.id, null, "initial")
              }
            }
          }
        })
        .catch(() => {
          if (
            activeRequests.current.get(collectionId) !== controller ||
            controller.signal.aborted
          ) {
            return
          }
          failedLoads.current.set(collectionId, { cursor, direction })
          const previous = states.current.get(collectionId) ?? EMPTY_COLLECTION
          publish(collectionId, {
            ...previous,
            loading: true,
            error: "Could not load items",
          })
        })
        .finally(() => {
          if (activeRequests.current.get(collectionId) !== controller) return
          activeRequests.current.delete(collectionId)
          const latest = states.current.get(collectionId) ?? EMPTY_COLLECTION
          publish(collectionId, { ...latest, loading: false })
        })
    },
    [catalog, maximumItems, workspaceId, publish, repositoryId]
  )

  const activate = useCallback(
    (collectionId: CollectionId) => {
      activeCollections.current.add(collectionId)
      const state = states.current.get(collectionId)
      if (!state?.pages.length && !state?.error) {
        requestPage(collectionId, null, "initial")
      } else if (state) {
        for (const item of state.pages.flatMap(({ items }) => items)) {
          if (item.kind !== "project") continue
          const child = states.current.get(item.id)
          if (
            !activeRequests.current.has(item.id) &&
            !child?.pages.length &&
            !child?.error
          ) {
            requestPage(item.id, null, "initial")
          }
        }
      }

      return () => {
        activeCollections.current.delete(collectionId)
      }
    },
    [requestPage]
  )

  const loadNext = useCallback(
    (collectionId: CollectionId) => {
      const state = states.current.get(collectionId)
      const cursor = state?.pages.at(-1)?.nextCursor
      if (cursor) requestPage(collectionId, cursor, "next")
    },
    [requestPage]
  )

  const loadPrevious = useCallback(
    (collectionId: CollectionId) => {
      const state = states.current.get(collectionId)
      const cursor = state?.pages[0]?.previousCursor
      if (cursor) requestPage(collectionId, cursor, "previous")
    },
    [requestPage]
  )

  const retry = useCallback(
    (collectionId: CollectionId) => {
      const failed = failedLoads.current.get(collectionId)
      if (failed) requestPage(collectionId, failed.cursor, failed.direction)
    },
    [requestPage]
  )

  const invalidate = useCallback(
    (...collectionIds: CollectionId[]) => {
      for (const collectionId of new Set(collectionIds)) {
        const state = states.current.get(collectionId)
        if (!state) continue
        activeRequests.current.get(collectionId)?.abort()
        activeRequests.current.delete(collectionId)
        requestPage(collectionId, null, "initial")
      }
    },
    [requestPage]
  )

  useEffect(() => {
    mounted.current = true
    const requests = activeRequests.current
    return () => {
      mounted.current = false
      for (const controller of requests.values()) controller.abort()
      requests.clear()
    }
  }, [])

  return {
    collections,
    emptyCollection: EMPTY_COLLECTION,
    activate,
    invalidate,
    loadNext,
    loadPrevious,
    retry,
  }
}

function trimPages(
  pages: Page<RepositoryItem>[],
  direction: Exclude<LoadDirection, "initial">,
  maximumItems: number
): Page<RepositoryItem>[] {
  const retained = [...pages]
  let count = retained.reduce((total, page) => total + page.items.length, 0)

  while (count > maximumItems && retained.length > 1) {
    const removed = direction === "next" ? retained.shift() : retained.pop()
    count -= removed?.items.length ?? 0
  }

  return retained
}
