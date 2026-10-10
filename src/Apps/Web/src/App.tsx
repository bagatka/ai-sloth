import * as React from "react"

import ChatScreen from "@/showcase/chat-screen"

// The component catalog is a tool for styling, so it loads only when someone opens /components.
const Catalog = React.lazy(() => import("@/showcase/catalog"))

export default function App() {
  if (location.pathname.startsWith("/components")) {
    return (
      <React.Suspense fallback={null}>
        <Catalog />
      </React.Suspense>
    )
  }

  return <ChatScreen />
}
