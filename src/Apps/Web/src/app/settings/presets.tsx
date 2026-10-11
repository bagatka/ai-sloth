import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { PencilSimpleIcon, PlusIcon } from "@phosphor-icons/react"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import { Field, FieldError, FieldLabel } from "@/components/ui/field"
import { Input } from "@/components/ui/input"
import { Skeleton } from "@/components/ui/skeleton"
import { toast } from "@/components/ui/toast"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import { ApiError, answer, api, messageOf, type Schemas } from "../api"
import { ChooseAgentDialog } from "../choose-agent"
import {
  agentAccounts,
  harnesses as harnessesQuery,
  presets as presetsQuery,
} from "../queries"
import { NoRows, RemoveButton, Row, Rows, Section } from "./section"

// Presets: the agents someone starts chats with most, each an agent, the account that pays, and its
// model. They're a person's own, and show in every workspace their account works in.

type Workspace = Schemas["WorkspaceSummary"]
type Preset = Schemas["PresetSummary"]

export function PresetsSection({ workspace }: { workspace: Workspace }) {
  const queryClient = useQueryClient()
  const list = useQuery(presetsQuery(workspace.id))
  const harnesses = useQuery(harnessesQuery).data
  const accounts = useQuery(agentAccounts(workspace.id)).data
  const [adding, setAdding] = React.useState(false)
  const remove = useMutation({
    mutationFn: (id: string) =>
      answer(api.DELETE("/presets/{id}", { params: { path: { id } } })),
    onSuccess: () =>
      queryClient.invalidateQueries({
        queryKey: presetsQuery(workspace.id).queryKey,
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
      id="presets"
      title="Presets"
      description="The agents you start chats with most: each one an agent, the account that pays, and its model. They're yours alone."
      action={
        <>
          <Button variant="outline" size="sm" onClick={() => setAdding(true)}>
            <PlusIcon data-icon="inline-start" />
            Add
          </Button>
          <ChooseAgentDialog
            workspace={workspace}
            open={adding}
            onOpenChange={setAdding}
            purpose="save"
          />
        </>
      }
    >
      <Rows label="Presets">
        {list.isPending ? (
          <NoRows>
            <Skeleton className="h-4 w-48" />
          </NoRows>
        ) : (list.data ?? []).length === 0 ? (
          <NoRows>
            None yet. Save the agents and models you use most, and a new chat
            offers them first.
          </NoRows>
        ) : (
          (list.data ?? []).map((preset) => (
            <Row
              key={preset.id}
              title={preset.name}
              detail={[
                harnesses?.find((harness) => harness.id === preset.harness)
                  ?.name ?? preset.harness,
                preset.model ?? "default model",
                accounts?.find((account) => account.id === preset.account)
                  ?.name,
              ]
                .filter(Boolean)
                .join(", ")}
            >
              <RenameButton workspace={workspace} preset={preset} />
              <RemoveButton
                what={preset.name}
                consequence="Chats started from it keep running; new chats can't start from it."
                pending={remove.isPending && remove.variables === preset.id}
                onRemove={() => remove.mutate(preset.id)}
              />
            </Row>
          ))
        )}
      </Rows>
    </Section>
  )
}

function RenameButton({
  workspace,
  preset,
}: {
  workspace: Workspace
  preset: Preset
}) {
  const queryClient = useQueryClient()
  const [open, setOpen] = React.useState(false)
  const [name, setName] = React.useState(preset.name)
  const rename = useMutation({
    mutationFn: () =>
      answer(
        api.PUT("/presets/{id}/name", {
          params: { path: { id: preset.id } },
          body: { name },
        })
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: presetsQuery(workspace.id).queryKey,
      })
      setOpen(false)
    },
  })
  const error = rename.error instanceof ApiError ? rename.error : null

  return (
    <Dialog
      open={open}
      onOpenChange={(opening) => {
        setOpen(opening)
        if (opening) {
          setName(preset.name)
          rename.reset()
        }
      }}
    >
      <Tooltip>
        <TooltipTrigger
          render={
            <DialogTrigger
              render={
                <Button
                  variant="ghost"
                  size="icon-sm"
                  aria-label={`Rename ${preset.name}`}
                />
              }
            />
          }
        >
          <PencilSimpleIcon />
        </TooltipTrigger>
        <TooltipContent>Rename</TooltipContent>
      </Tooltip>
      <DialogContent>
        <form
          className="contents"
          onSubmit={(event) => {
            event.preventDefault()
            if (name.trim()) {
              rename.mutate()
            }
          }}
        >
          <DialogHeader>
            <DialogTitle>Rename {preset.name}</DialogTitle>
          </DialogHeader>
          <Field data-invalid={rename.isError || undefined}>
            <FieldLabel htmlFor="preset-name">Name</FieldLabel>
            <Input
              id="preset-name"
              value={name}
              onChange={(event) => setName(event.target.value)}
              aria-invalid={rename.isError || undefined}
            />
            {rename.isError && (
              <FieldError>
                {error?.fields.name?.[0] ?? messageOf(rename.error)}
              </FieldError>
            )}
          </Field>
          <DialogFooter>
            <Button
              type="submit"
              loading={rename.isPending}
              disabled={!name.trim()}
            >
              Rename
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
