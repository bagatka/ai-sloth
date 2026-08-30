import { useEffect, useState } from "react"
import type { Authentication } from "@/authentication"
import { useGitHub } from "@/github"
import { AppSidebar } from "@/components/app-sidebar"
import {
  SidebarInset,
  SidebarProvider,
  SidebarTrigger,
} from "@/components/ui/sidebar"
import type { Workspaces } from "@/workspaces"
import { createRepositoryCatalog } from "@/repository-navigation"
import { SessionWorkspace, type SessionSelection } from "@/session"

export function WorkspaceShell({
  authentication,
  workspaces,
}: {
  authentication: Authentication
  workspaces: Workspaces
}) {
  const [catalog] = useState(() =>
    createRepositoryCatalog(authentication.request)
  )
  const github = useGitHub(authentication.request)
  const [initialSelection] = useState(readSelection)
  const [activeWorkspaceId, setActiveWorkspaceId] = useState(() =>
    workspaces.workspaces.some(({ id }) => id === initialSelection?.workspaceId)
      ? initialSelection!.workspaceId
      : (workspaces.workspaces[0]?.id ?? "")
  )
  const [selection, setSelection] = useState<SessionSelection | null>(
    initialSelection
  )
  const activeWorkspace =
    workspaces.workspaces.find(
      (workspace) => workspace.id === activeWorkspaceId
    ) ?? workspaces.workspaces[0]
  const account = authentication.account

  useEffect(() => {
    const navigate = () => {
      const next = readSelection()
      setSelection(next)
      if (
        next &&
        workspaces.workspaces.some(({ id }) => id === next.workspaceId)
      ) {
        setActiveWorkspaceId(next.workspaceId)
      }
    }
    window.addEventListener("popstate", navigate)
    return () => window.removeEventListener("popstate", navigate)
  }, [workspaces.workspaces])

  if (!activeWorkspace || !account) return null

  async function signOut(): Promise<string | null> {
    const outcome = await authentication.signOut()
    return outcome.ok ? null : outcome.error
  }

  function selectSession(sessionId: string) {
    const next = { workspaceId: activeWorkspace.id, sessionId }
    setSelection(next)
    writeSelection(next)
  }

  function selectWorkspace(workspaceId: string) {
    setActiveWorkspaceId(workspaceId)
    setSelection(null)
    writeSelection(null)
  }

  return (
    <SidebarProvider>
      <AppSidebar
        activeWorkspace={activeWorkspace}
        catalog={catalog}
        email={account.email}
        github={github}
        workspaces={workspaces}
        signingOut={authentication.pending}
        onSelectWorkspace={(workspace) => selectWorkspace(workspace.id)}
        onSelectSession={selectSession}
        onSignOut={signOut}
      />
      <SidebarInset className="h-svh min-h-0 overflow-hidden">
        <header className="flex h-12 shrink-0 items-center border-b px-4">
          <SidebarTrigger className="-ml-1" />
        </header>
        <div className="flex min-h-0 flex-1" aria-label="Workspace">
          {selection && selection.workspaceId === activeWorkspace.id ? (
            <SessionWorkspace
              key={`${selection.workspaceId}:${selection.sessionId}`}
              request={authentication.request}
              workspaceId={selection.workspaceId}
              sessionId={selection.sessionId}
            />
          ) : (
            <div className="flex flex-1 items-center justify-center p-6 text-sm text-muted-foreground">
              Select or create a session to begin.
            </div>
          )}
        </div>
      </SidebarInset>
    </SidebarProvider>
  )
}

function readSelection(): SessionSelection | null {
  const query = new URLSearchParams(window.location.search)
  const workspaceId = query.get("workspace")
  const sessionId = query.get("session")
  return workspaceId && sessionId ? { workspaceId, sessionId } : null
}

function writeSelection(selection: SessionSelection | null): void {
  const url = new URL(window.location.href)
  if (selection) {
    url.searchParams.set("workspace", selection.workspaceId)
    url.searchParams.set("session", selection.sessionId)
  } else {
    url.searchParams.delete("workspace")
    url.searchParams.delete("session")
  }
  window.history.pushState(null, "", url)
}
