import { useState } from "react"
import { Building2, KeyRound } from "lucide-react"
import type { Workspace, Workspaces } from "@/workspaces"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import {
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
} from "@/components/ui/field"
import { Input } from "@/components/ui/input"

export function WorkspaceOnboarding({
  workspaces,
  onComplete,
}: {
  workspaces: Workspaces
  onComplete?: (workspace: Workspace) => void
}) {
  return (
    <section className="w-full max-w-4xl">
      <header className="mx-auto mb-8 max-w-xl text-center">
        <p className="mb-2 text-xs font-medium tracking-widest text-primary uppercase">
          One last step
        </p>
        <h1 className="font-heading text-3xl font-semibold tracking-tight">
          Choose your workspace
        </h1>
        <p className="mt-3 text-sm leading-relaxed text-muted-foreground">
          Create a new workspace or join your team with an invitation code.
        </p>
      </header>

      <div className="grid gap-4 md:grid-cols-2">
        <CreateWorkspace workspaces={workspaces} onComplete={onComplete} />
        <JoinWorkspace workspaces={workspaces} onComplete={onComplete} />
      </div>
    </section>
  )
}

function CreateWorkspace({
  workspaces,
  onComplete,
}: {
  workspaces: Workspaces
  onComplete?: (workspace: Workspace) => void
}) {
  const [error, setError] = useState<string | null>(null)

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (workspaces.pending) return

    setError(null)
    const data = new FormData(event.currentTarget)
    const outcome = await workspaces.create(
      String(data.get("workspaceName") ?? "")
    )
    if (outcome.ok) onComplete?.(outcome.value)
    else setError(outcome.error)
  }

  return (
    <Card>
      <CardHeader>
        <div className="mb-3 flex size-10 items-center justify-center bg-primary/10 text-primary">
          <Building2 className="size-5" aria-hidden="true" />
        </div>
        <CardTitle>Create a workspace</CardTitle>
        <CardDescription>
          Start a new workspace for your projects and team.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={submit}>
          <FieldGroup>
            <Field>
              <FieldLabel htmlFor="workspace-name">Workspace name</FieldLabel>
              <Input
                id="workspace-name"
                name="workspaceName"
                autoComplete="workspace-title"
                maxLength={100}
                placeholder="Acme"
                disabled={workspaces.pending !== null}
                required
              />
            </Field>
            {error && <FieldError>{error}</FieldError>}
            <Button type="submit" disabled={workspaces.pending !== null}>
              {workspaces.pending === "create"
                ? "Creating…"
                : "Create workspace"}
            </Button>
          </FieldGroup>
        </form>
      </CardContent>
    </Card>
  )
}

function JoinWorkspace({
  workspaces,
  onComplete,
}: {
  workspaces: Workspaces
  onComplete?: (workspace: Workspace) => void
}) {
  const [error, setError] = useState<string | null>(null)

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (workspaces.pending) return

    setError(null)
    const data = new FormData(event.currentTarget)
    const outcome = await workspaces.join(
      String(data.get("invitationCode") ?? "").trim()
    )
    if (outcome.ok) onComplete?.(outcome.value)
    else setError(outcome.error)
  }

  return (
    <Card>
      <CardHeader>
        <div className="mb-3 flex size-10 items-center justify-center bg-primary/10 text-primary">
          <KeyRound className="size-5" aria-hidden="true" />
        </div>
        <CardTitle>Join a workspace</CardTitle>
        <CardDescription>
          Enter the invitation code shared by a team member.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={submit}>
          <FieldGroup>
            <Field>
              <FieldLabel htmlFor="invitation-code">Invitation code</FieldLabel>
              <Input
                id="invitation-code"
                name="invitationCode"
                autoComplete="off"
                maxLength={128}
                placeholder="asl_workspace_invite_…"
                spellCheck={false}
                disabled={workspaces.pending !== null}
                required
              />
            </Field>
            {error && <FieldError>{error}</FieldError>}
            <Button type="submit" disabled={workspaces.pending !== null}>
              {workspaces.pending === "join" ? "Joining…" : "Join workspace"}
            </Button>
          </FieldGroup>
        </form>
      </CardContent>
    </Card>
  )
}
