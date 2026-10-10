import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { UserPlusIcon } from "@phosphor-icons/react"

import { Alert, AlertDescription } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import { Field, FieldDescription, FieldLabel } from "@/components/ui/field"
import { Skeleton } from "@/components/ui/skeleton"
import { toast } from "@/components/ui/toast"
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group"
import { answer, api, messageOf, type Schemas } from "../api"
import { me, people, workspaceAccess } from "../queries"
import {
  CopyableCode,
  NoRows,
  RemoveButton,
  Row,
  Rows,
  Section,
} from "./section"

// People: who has access to the workspace, and inviting more with a one-time code.

type Workspace = Schemas["WorkspaceSummary"]
type Access = Schemas["AccessLevel"]

// What each access level lets someone do; each includes the ones before it.
const accessWords: Record<Access, string> = {
  Read: "Sees its chats",
  Write: "Sees its chats and works in them",
  Manage: "Works in its chats and decides who has access",
}

const day = new Intl.DateTimeFormat("en", {
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
})

export function PeopleSection({ workspace }: { workspace: Workspace }) {
  const queryClient = useQueryClient()
  const manager = workspace.access === "Manage"
  const person = useQuery(me).data
  const grants = useQuery(workspaceAccess(workspace.id))
  const names = useQuery(
    people((grants.data ?? []).map((grant) => grant.userId))
  ).data
  const revoke = useMutation({
    mutationFn: (userId: string) =>
      answer(
        api.DELETE("/workspaces/{workspaceId}/access/{userId}", {
          params: { path: { workspaceId: workspace.id, userId } },
        })
      ),
    onSuccess: () =>
      queryClient.invalidateQueries({
        queryKey: workspaceAccess(workspace.id).queryKey,
      }),
    onError: (error) =>
      toast.add({
        type: "error",
        title: "Access not ended",
        description: messageOf(error),
      }),
  })

  return (
    <Section
      id="people"
      title="People"
      description={`Who has access to ${workspace.name}.`}
      action={manager && <Invite workspace={workspace} />}
    >
      <Rows label="People">
        {grants.isPending ? (
          <NoRows>
            <Skeleton className="h-4 w-40" />
          </NoRows>
        ) : grants.isError ? (
          <NoRows>{messageOf(grants.error)}</NoRows>
        ) : (
          (grants.data ?? []).map((grant) => {
            const name =
              names?.find((someone) => someone.id === grant.userId)?.name ??
              "Someone"
            const you = grant.userId === person?.id
            return (
              <Row
                key={grant.userId}
                title={you ? `${name} (you)` : name}
                detail={`${grant.access}: ${accessWords[grant.access].toLowerCase()}`}
              >
                {/* Not handled: leaving a workspace, which ends one's own access. */}
                {manager && !you && (
                  <RemoveButton
                    what={name}
                    consequence={`They no longer see ${workspace.name} or work in its chats. An invite gives access again.`}
                    pending={
                      revoke.isPending && revoke.variables === grant.userId
                    }
                    onRemove={() => revoke.mutate(grant.userId)}
                  />
                )}
              </Row>
            )
          })
        )}
      </Rows>
    </Section>
  )
}

function Invite({ workspace }: { workspace: Workspace }) {
  const [access, setAccess] = React.useState<Access>("Write")
  const invite = useMutation({
    mutationFn: () =>
      answer(
        api.POST("/workspaces/{workspaceId}/invites", {
          params: { path: { workspaceId: workspace.id } },
          body: { access },
        })
      ),
  })

  return (
    <Dialog
      onOpenChange={(opening) => {
        if (opening) {
          invite.reset()
        }
      }}
    >
      <DialogTrigger render={<Button variant="outline" size="sm" />}>
        <UserPlusIcon data-icon="inline-start" />
        Invite
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Invite someone to {workspace.name}</DialogTitle>
          <DialogDescription>
            An invite is a code that works once, for a week. They sign in with
            it, or join with it from the workspace menu if they already use
            AiSloth here.
          </DialogDescription>
        </DialogHeader>
        {invite.data ? (
          <>
            <CopyableCode code={invite.data.code} />
            <p className="text-label text-muted-foreground">
              {invite.data.access}:{" "}
              {accessWords[invite.data.access].toLowerCase()}. Works until{" "}
              {day.format(new Date(invite.data.expiresAt))}.
            </p>
            <DialogFooter>
              <DialogClose render={<Button />}>Done</DialogClose>
            </DialogFooter>
          </>
        ) : (
          <form
            className="contents"
            onSubmit={(event) => {
              event.preventDefault()
              invite.mutate()
            }}
          >
            <Field>
              <FieldLabel>Access</FieldLabel>
              <ToggleGroup
                value={[access]}
                onValueChange={(value) =>
                  value[0] && setAccess(value[0] as Access)
                }
                variant="outline"
                spacing={0}
                size="sm"
                className="w-fit"
              >
                <ToggleGroupItem value="Read">Read</ToggleGroupItem>
                <ToggleGroupItem value="Write">Write</ToggleGroupItem>
                <ToggleGroupItem value="Manage">Manage</ToggleGroupItem>
              </ToggleGroup>
              <FieldDescription>{accessWords[access]}.</FieldDescription>
            </Field>
            {invite.isError && (
              <Alert variant="destructive">
                <AlertDescription>{messageOf(invite.error)}</AlertDescription>
              </Alert>
            )}
            <DialogFooter>
              <Button type="submit" loading={invite.isPending}>
                Create invite
              </Button>
            </DialogFooter>
          </form>
        )}
      </DialogContent>
    </Dialog>
  )
}
