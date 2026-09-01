import { useState } from "react"
import { type Authentication, useAuthentication } from "@/authentication"
import { AuthenticationForm } from "@/components/authentication-form"
import { ModeToggle } from "@/components/mode-toggle"
import { WorkspaceOnboarding } from "@/components/workspace-onboarding"
import { ThemeProvider } from "@/components/theme-provider"
import { Button } from "@/components/ui/button"
import { TooltipProvider } from "@/components/ui/tooltip"
import { WorkspaceShell } from "@/components/workspace-shell"
import { useWorkspaces } from "@/workspaces"

export function App() {
  const authentication = useAuthentication()

  return (
    <ThemeProvider defaultTheme="dark" storageKey="ai-sloth-theme">
      <TooltipProvider>
        {authentication.authenticated ? (
          <SignedIn authentication={authentication} />
        ) : (
          <main className="relative flex min-h-svh items-center justify-center bg-muted p-6 md:p-10">
            <div className="absolute top-4 right-4">
              <ModeToggle />
            </div>
            <AuthenticationForm
              authentication={authentication}
              className="w-full max-w-3xl"
            />
          </main>
        )}
      </TooltipProvider>
    </ThemeProvider>
  )
}

function SignedIn({ authentication }: { authentication: Authentication }) {
  const workspaces = useWorkspaces(authentication.request)

  if (workspaces.status === "ready" && workspaces.workspaces.length > 0) {
    return (
      <WorkspaceShell authentication={authentication} workspaces={workspaces} />
    )
  }

  return (
    <main className="relative flex min-h-svh items-center justify-center bg-muted p-6 md:p-10">
      <div className="absolute top-4 right-4">
        <SignOutButton authentication={authentication} />
      </div>
      {workspaces.status === "loading" ? (
        <p className="text-sm text-muted-foreground">Loading workspaces…</p>
      ) : workspaces.status === "error" ? (
        <div className="text-center">
          <p className="mb-4 text-sm text-destructive">{workspaces.error}</p>
          <Button type="button" onClick={workspaces.reload}>
            Try again
          </Button>
        </div>
      ) : (
        <WorkspaceOnboarding workspaces={workspaces} />
      )}
    </main>
  )
}

function SignOutButton({ authentication }: { authentication: Authentication }) {
  const [error, setError] = useState<string | null>(null)

  async function signOut() {
    setError(null)
    const outcome = await authentication.signOut()
    if (!outcome.ok) setError(outcome.error)
  }

  return (
    <Button
      type="button"
      variant="outline"
      onClick={signOut}
      disabled={authentication.pending}
      title={error ?? undefined}
    >
      {authentication.pending
        ? "Signing out…"
        : error
          ? "Retry sign out"
          : "Sign out"}
    </Button>
  )
}

export default App
