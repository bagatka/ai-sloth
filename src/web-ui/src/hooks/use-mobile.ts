import * as React from "react"

const MOBILE_BREAKPOINT = 768
const MOBILE_QUERY = `(max-width: ${MOBILE_BREAKPOINT - 1}px)`

export function useIsMobile() {
  const [isMobile, setIsMobile] = React.useState(
    () => window.matchMedia(MOBILE_QUERY).matches
  )

  React.useEffect(() => {
    const query = window.matchMedia(MOBILE_QUERY)
    const update = () => setIsMobile(query.matches)
    query.addEventListener("change", update)
    return () => query.removeEventListener("change", update)
  }, [])

  return isMobile
}
