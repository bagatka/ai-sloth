import { useCallback, useEffect, useRef, useState } from "react"
import type { Page } from ".."

type PageLoader<T> = (
  cursor: string | null,
  signal: AbortSignal
) => Promise<Page<T>>

type LoadDirection = "initial" | "next" | "previous"

type FailedLoad = {
  cursor: string | null
  direction: LoadDirection
}

export function usePagedWindow<T>(
  loadPage: PageLoader<T>,
  maximumItems: number
) {
  const activeRequest = useRef<AbortController | null>(null)
  const failedLoad = useRef<FailedLoad | null>(null)
  const [pages, setPages] = useState<Page<T>[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const requestPage = useCallback(
    (cursor: string | null, direction: LoadDirection) => {
      if (activeRequest.current) return

      const controller = new AbortController()
      activeRequest.current = controller

      void loadPage(cursor, controller.signal)
        .then((page) => {
          if (activeRequest.current !== controller) return
          failedLoad.current = null
          setPages((current) => {
            if (direction === "initial") return [page]
            const next =
              direction === "next" ? [...current, page] : [page, ...current]
            return trimPages(next, direction, maximumItems)
          })
        })
        .catch(() => {
          if (
            activeRequest.current !== controller ||
            controller.signal.aborted
          ) {
            return
          }
          failedLoad.current = { cursor, direction }
          setError("Could not load items")
        })
        .finally(() => {
          if (activeRequest.current === controller) {
            activeRequest.current = null
            setLoading(false)
          }
        })
    },
    [loadPage, maximumItems]
  )

  const beginLoad = useCallback(
    (cursor: string, direction: Exclude<LoadDirection, "initial">) => {
      setLoading(true)
      setError(null)
      requestPage(cursor, direction)
    },
    [requestPage]
  )

  const loadNext = useCallback(() => {
    const cursor = pages.at(-1)?.nextCursor
    if (cursor) beginLoad(cursor, "next")
  }, [beginLoad, pages])

  const loadPrevious = useCallback(() => {
    const cursor = pages[0]?.previousCursor
    if (cursor) beginLoad(cursor, "previous")
  }, [beginLoad, pages])

  const retry = useCallback(() => {
    const failed = failedLoad.current
    if (!failed) return
    setLoading(true)
    setError(null)
    requestPage(failed.cursor, failed.direction)
  }, [requestPage])

  useEffect(() => {
    requestPage(null, "initial")
    return () => {
      activeRequest.current?.abort()
      activeRequest.current = null
    }
  }, [requestPage])

  return {
    items: pages.flatMap((page) => page.items),
    hasPrevious: Boolean(pages[0]?.previousCursor),
    hasNext: Boolean(pages.at(-1)?.nextCursor),
    loading,
    error,
    loadNext,
    loadPrevious,
    retry,
  }
}

function trimPages<T>(
  pages: Page<T>[],
  direction: Exclude<LoadDirection, "initial">,
  maximumItems: number
): Page<T>[] {
  const retained = [...pages]
  let count = retained.reduce((total, page) => total + page.items.length, 0)

  while (count > maximumItems && retained.length > 1) {
    const removed = direction === "next" ? retained.shift() : retained.pop()
    count -= removed?.items.length ?? 0
  }

  return retained
}
