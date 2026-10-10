import { MutationCache, QueryCache, QueryClient } from "@tanstack/react-query"
import {
  createRootRouteWithContext,
  createRoute,
  createRouter,
  lazyRouteComponent,
  Outlet,
  redirect,
} from "@tanstack/react-router"

import { ApiError, isSignedOut } from "./api"
import { ChatPage } from "./chat/chat-page"
import { NewChatPage } from "./new-chat"
import { appBase } from "./host"
import { me, nooks } from "./queries"
import { onChatsChange } from "./workspace-chats"
import { AppShell } from "./shell"
import { SignInPage } from "./sign-in"

// The app's composition root: one query cache for everything read from the host, and the routes.
// Whenever the host says this browser isn't signed in (its session ended or expired), the app
// forgets what it read and asks to sign in, coming back to the same place afterwards.

export const queryClient = new QueryClient({
  queryCache: new QueryCache({ onError: signInAgainIfSignedOut }),
  mutationCache: new MutationCache({ onError: signInAgainIfSignedOut }),
  defaultOptions: {
    queries: {
      // A refusal won't change by asking again; a host that didn't answer might.
      retry: (failures, error) =>
        !(error instanceof ApiError && error.status < 500) && failures < 2,
    },
  },
})

// A workspace's nooks change state along with its chats, so the list of its nooks is read again
// whenever its chats change.
onChatsChange((workspaceId) => {
  void queryClient.invalidateQueries({
    queryKey: nooks(workspaceId).queryKey,
  })
})

/** Where in the app the browser is, relative to the app's address, for coming back after signing in. */
export function returnPath(): string {
  const path = location.pathname.startsWith(appBase)
    ? location.pathname.slice(appBase.length)
    : ""
  return path === "sign-in" ? "" : path
}

function signInAgainIfSignedOut(error: unknown) {
  const signingIn = location.pathname === appBase + "sign-in"
  if (!isSignedOut(error) || signingIn) {
    return
  }
  queryClient.clear()
  void router.navigate({
    to: "/sign-in",
    search: { return_to: returnPath() || undefined },
  })
}

type SignInSearch = { return_to?: string; sign_in_error?: string }

const root = createRootRouteWithContext<{ queryClient: QueryClient }>()({
  component: Outlet,
})

const signIn = createRoute({
  getParentRoute: () => root,
  path: "sign-in",
  validateSearch: (search: Record<string, unknown>): SignInSearch => ({
    return_to:
      typeof search.return_to === "string" ? search.return_to : undefined,
    sign_in_error:
      typeof search.sign_in_error === "string"
        ? search.sign_in_error
        : undefined,
  }),
  component: SignInPage,
})

// Everything else needs a signed-in person; who they are is asked once and kept.
const signedIn = createRoute({
  getParentRoute: () => root,
  id: "signed-in",
  validateSearch: (search: Record<string, unknown>): SignInSearch => ({
    sign_in_error:
      typeof search.sign_in_error === "string"
        ? search.sign_in_error
        : undefined,
  }),
  beforeLoad: async ({ context, search }) => {
    try {
      await context.queryClient.ensureQueryData(me)
    } catch (error) {
      if (isSignedOut(error)) {
        throw redirect({
          to: "/sign-in",
          search: {
            return_to: returnPath() || undefined,
            sign_in_error: search.sign_in_error,
          },
        })
      }
      throw error
    }
  },
  component: AppShell,
})

const newChat = createRoute({
  getParentRoute: () => signedIn,
  path: "/",
  component: NewChatPage,
})

export const chatRoute = createRoute({
  getParentRoute: () => signedIn,
  path: "chats/$chatId",
  component: ChatPage,
})

// The design playground: a sample chat screen and every component, for styling, in development only.
const design = import.meta.env.DEV
  ? [
      createRoute({
        getParentRoute: () => root,
        path: "design",
        component: lazyRouteComponent(() => import("@/showcase/chat-screen")),
      }),
      createRoute({
        getParentRoute: () => root,
        path: "design/components",
        component: lazyRouteComponent(() => import("@/showcase/catalog")),
      }),
    ]
  : []

export const router = createRouter({
  routeTree: root.addChildren([
    signIn,
    signedIn.addChildren([newChat, chatRoute]),
    ...design,
  ]),
  basepath: appBase,
  context: { queryClient },
  defaultPreload: "intent",
})

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router
  }
}
