import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { PlusIcon } from "@phosphor-icons/react"

import { Alert, AlertDescription } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
} from "@/components/ui/field"
import { Input } from "@/components/ui/input"
import { Skeleton } from "@/components/ui/skeleton"
import { toast } from "@/components/ui/toast"
import { ApiError, answer, api, messageOf, type Schemas } from "../api"
import { people, secrets as secretsQuery } from "../queries"
import { NoRows, RemoveButton, Row, Rows, Section } from "./section"

// Secrets: environment variables every process in the workspace's nooks gets, agents included,
// such as GH_TOKEN for gh. Their values are never shown again; setting a name again replaces it.

type Workspace = Schemas["WorkspaceSummary"]

// The interface's words are English, so its dates are too.
const day = new Intl.DateTimeFormat("en", { month: "short", day: "numeric" })

export function SecretsSection({ workspace }: { workspace: Workspace }) {
  const queryClient = useQueryClient()
  const manager = workspace.access === "Manage"
  const list = useQuery(secretsQuery(workspace.id))
  const setters = useQuery(
    people((list.data ?? []).map((secret) => secret.setBy))
  ).data
  const remove = useMutation({
    mutationFn: (name: string) =>
      answer(
        api.DELETE("/workspaces/{workspaceId}/secrets/{name}", {
          params: { path: { workspaceId: workspace.id, name } },
        })
      ),
    onSuccess: () =>
      queryClient.invalidateQueries({
        queryKey: secretsQuery(workspace.id).queryKey,
      }),
    onError: (error) =>
      toast.add({
        type: "error",
        title: "Not removed",
        description: messageOf(error),
      }),
  })

  return (
    <Section
      id="secrets"
      title="Secrets"
      description={`Environment variables every process in ${workspace.name}'s nooks gets, agents included, such as GH_TOKEN for gh. Everyone who works in a nook can read them.`}
      action={manager && <SetSecret workspace={workspace} />}
    >
      <Rows label="Secrets">
        {list.isPending ? (
          <NoRows>
            <Skeleton className="h-4 w-40" />
          </NoRows>
        ) : (list.data ?? []).length === 0 ? (
          <NoRows>None yet.</NoRows>
        ) : (
          (list.data ?? []).map((secret) => (
            <Row
              key={secret.id}
              title={<span className="font-mono">{secret.name}</span>}
              detail={`Set by ${setters?.find((person) => person.id === secret.setBy)?.name ?? "someone"} on ${day.format(new Date(secret.setAt))}`}
            >
              {manager && (
                <RemoveButton
                  what={secret.name}
                  consequence="Processes started from now on don't get it. Ones already running keep it."
                  pending={remove.isPending && remove.variables === secret.name}
                  onRemove={() => remove.mutate(secret.name)}
                />
              )}
            </Row>
          ))
        )}
      </Rows>
    </Section>
  )
}

function SetSecret({ workspace }: { workspace: Workspace }) {
  const queryClient = useQueryClient()
  const [open, setOpen] = React.useState(false)
  const [name, setName] = React.useState("")
  const [value, setValue] = React.useState("")
  const set = useMutation({
    mutationFn: () =>
      answer(
        api.PUT("/workspaces/{workspaceId}/secrets/{name}", {
          params: { path: { workspaceId: workspace.id, name: name.trim() } },
          body: { value },
        })
      ),
    onSuccess: (secret) => {
      void queryClient.invalidateQueries({
        queryKey: secretsQuery(workspace.id).queryKey,
      })
      toast.add({ type: "success", title: `Set ${secret.name}` })
      setOpen(false)
    },
  })
  const error = set.error instanceof ApiError ? set.error : null
  const nameError = error?.fields.name?.[0]
  const valueError = error?.fields.value?.[0]

  return (
    <Dialog
      open={open}
      onOpenChange={(opening) => {
        setOpen(opening)
        if (opening) {
          setName("")
          setValue("")
          set.reset()
        }
      }}
    >
      <DialogTrigger render={<Button variant="outline" size="sm" />}>
        <PlusIcon data-icon="inline-start" />
        Set
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Set a secret</DialogTitle>
          <DialogDescription>
            A name that's set already gets the new value. The value is never
            shown again.
          </DialogDescription>
        </DialogHeader>
        <form
          className="contents"
          onSubmit={(event) => {
            event.preventDefault()
            if (name.trim() && value) {
              set.mutate()
            }
          }}
        >
          <FieldGroup>
            <Field data-invalid={!!nameError || undefined}>
              <FieldLabel htmlFor="secret-name">Name</FieldLabel>
              <Input
                id="secret-name"
                value={name}
                placeholder="GH_TOKEN"
                autoCapitalize="characters"
                spellCheck={false}
                className="font-mono"
                onChange={(event) => setName(event.target.value)}
                aria-invalid={!!nameError || undefined}
              />
              {nameError ? (
                <FieldError>{nameError}</FieldError>
              ) : (
                <FieldDescription>
                  As processes see it: letters, digits, and underscores.
                </FieldDescription>
              )}
            </Field>
            <Field data-invalid={!!valueError || undefined}>
              <FieldLabel htmlFor="secret-value">Value</FieldLabel>
              <Input
                id="secret-value"
                type="password"
                autoComplete="off"
                value={value}
                onChange={(event) => setValue(event.target.value)}
                aria-invalid={!!valueError || undefined}
              />
              {valueError && <FieldError>{valueError}</FieldError>}
            </Field>
            {set.isError && !nameError && !valueError && (
              <Alert variant="destructive">
                <AlertDescription>{messageOf(set.error)}</AlertDescription>
              </Alert>
            )}
          </FieldGroup>
          <DialogFooter>
            <Button
              type="submit"
              loading={set.isPending}
              disabled={!name.trim() || !value}
            >
              Set secret
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
