import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { PencilSimpleIcon, PlusIcon } from "@phosphor-icons/react"

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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Skeleton } from "@/components/ui/skeleton"
import { toast } from "@/components/ui/toast"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import { ApiError, answer, api, messageOf, type Schemas } from "../api"
import { accountsFor, ModelPicker } from "../model-picker"
import {
  fitThinking,
  modelLabel,
  thinkingLabel,
  type Choice,
  type Chosen,
} from "../models"
import {
  agentAccounts,
  harnesses as harnessesQuery,
  models as modelsQuery,
  presets as presetsQuery,
} from "../queries"
import { NoRows, RemoveButton, Row, Rows, Section } from "./section"

// Presets: the agents someone starts chats with most, each an agent, the account that pays, and its
// model, at the top of every model picker. They're a person's own, and show in every workspace their
// account works in.

type Workspace = Schemas["WorkspaceSummary"]
type Preset = Schemas["PresetSummary"]

export function PresetsSection({ workspace }: { workspace: Workspace }) {
  const queryClient = useQueryClient()
  const list = useQuery(presetsQuery(workspace.id))
  const harnesses = useQuery(harnessesQuery).data
  const accounts = useQuery(agentAccounts(workspace.id)).data
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
      description="Shortcuts at the top of the model picker: an agent and its model, on the account that pays. They're yours alone."
      action={<AddPreset workspace={workspace} />}
    >
      <Rows label="Presets">
        {list.isPending ? (
          <NoRows>
            <Skeleton className="h-4 w-48" />
          </NoRows>
        ) : (list.data ?? []).length === 0 ? (
          <NoRows>
            None yet. Any model is a click away in the message box; save the
            ones you use most to find them first.
          </NoRows>
        ) : (
          (list.data ?? []).map((preset) => (
            <Row
              key={preset.id}
              title={preset.name}
              detail={[
                harnesses?.find((harness) => harness.id === preset.harness)
                  ?.name ?? preset.harness,
                preset.model,
                `${thinkingLabel(preset.thinking)} thinking`,
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

function AddPreset({ workspace }: { workspace: Workspace }) {
  const [open, setOpen] = React.useState(false)
  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger render={<Button variant="outline" size="sm" />}>
        <PlusIcon data-icon="inline-start" />
        Add
      </DialogTrigger>
      <DialogContent>
        {/* A new form each time the dialog opens. */}
        {open && (
          <PresetForm workspace={workspace} onSaved={() => setOpen(false)} />
        )}
      </DialogContent>
    </Dialog>
  )
}

function PresetForm({
  workspace,
  onSaved,
}: {
  workspace: Workspace
  onSaved: () => void
}) {
  const queryClient = useQueryClient()
  const accounts = useQuery(agentAccounts(workspace.id)).data ?? []
  const harnesses = (useQuery(harnessesQuery).data ?? []).filter(
    (harness) => accountsFor(accounts, harness).length > 0
  )
  const [chosen, setChosen] = React.useState<Choice | null>(null)
  const [name, setName] = React.useState("")

  // The agent holds while it's offered; until a model and level are picked, the first model its first
  // account lists for it, and its default level, shown as chosen.
  const harness =
    harnesses.find((found) => found.id === chosen?.harness) ?? harnesses[0]
  const offered = harness ? accountsFor(accounts, harness) : []
  const choice: Choice | null =
    harness && offered[0]
      ? chosen?.harness === harness.id &&
        offered.some((account) => account.id === chosen.account)
        ? chosen
        : {
            harness: harness.id,
            account: offered[0].id,
            model: null,
            thinking: null,
          }
      : null
  const models = useQuery({
    ...modelsQuery(workspace.id, choice?.harness ?? "", choice?.account ?? ""),
    enabled: !!choice,
  })
  const model = choice?.model ?? models.data?.[0]?.id ?? null
  const picked = models.data?.find((found) => found.id === model)
  // The levels the model takes, the agent's own until its list is in.
  const levels = picked?.thinkingLevels ?? harness?.thinkingLevels ?? []
  const ready: Chosen | null =
    choice && harness && model
      ? {
          ...choice,
          model,
          thinking: fitThinking(
            choice.thinking,
            levels,
            harness.defaultThinking
          ),
        }
      : null
  const defaultName =
    harness && ready
      ? [
          harness.name,
          picked ? spokenName(picked.name) : modelLabel(ready.model),
          levels.length > 0 && thinkingLabel(ready.thinking),
        ]
          .filter(Boolean)
          .join(" · ")
      : ""

  const save = useMutation({
    mutationFn: (of: Chosen) =>
      answer(
        api.POST("/workspaces/{workspaceId}/presets", {
          params: { path: { workspaceId: workspace.id } },
          body: { ...of, name: name.trim() || defaultName },
        })
      ),
    onSuccess: (preset) => {
      void queryClient.invalidateQueries({
        queryKey: presetsQuery(workspace.id).queryKey,
      })
      toast.add({ type: "success", title: `Saved ${preset.name}` })
      onSaved()
    },
  })
  const error = save.error instanceof ApiError ? save.error : null

  if (!harness || !choice) {
    return (
      <DialogHeader>
        <DialogTitle>Add a preset</DialogTitle>
        <DialogDescription>
          Add an agent account first: an API key, or your own plan.
        </DialogDescription>
      </DialogHeader>
    )
  }

  return (
    <form
      className="contents"
      onSubmit={(event) => {
        event.preventDefault()
        if (ready) {
          save.mutate(ready)
        }
      }}
    >
      <DialogHeader>
        <DialogTitle>Add a preset</DialogTitle>
        <DialogDescription>
          An agent and its model, at the top of the model picker when you start
          a chat.
        </DialogDescription>
      </DialogHeader>
      <FieldGroup>
        <Field>
          <FieldLabel>Agent</FieldLabel>
          <Select
            items={harnesses.map((found) => ({
              value: found.id,
              label: found.name,
            }))}
            value={harness.id}
            onValueChange={(value) => {
              const next = harnesses.find((found) => found.id === value)
              const first = next && accountsFor(accounts, next)[0]
              if (next && first) {
                setChosen({
                  harness: next.id,
                  account: first.id,
                  model: null,
                  thinking: null,
                })
                save.reset()
              }
            }}
          >
            <SelectTrigger aria-label="Agent">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {harnesses.map((found) => (
                <SelectItem key={found.id} value={found.id}>
                  {found.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </Field>
        <Field
          data-invalid={
            !!(error?.fields.model || error?.fields.account) || undefined
          }
        >
          <FieldLabel>Model</FieldLabel>
          <ModelPicker
            workspaceId={workspace.id}
            harness={harness}
            value={ready ?? choice}
            presets={[]}
            onChange={(next) => {
              setChosen(next)
              save.reset()
            }}
            variant="outline"
          />
          <FieldDescription>
            Every model of every account {harness.name} runs on, the ones it's
            made for first.
          </FieldDescription>
          {(error?.fields.model || error?.fields.account) && (
            <FieldError>
              {(error.fields.model ?? error.fields.account)?.[0]}
            </FieldError>
          )}
        </Field>
        {ready && levels.length > 0 && (
          <Field data-invalid={!!error?.fields.thinking || undefined}>
            <FieldLabel>Thinking</FieldLabel>
            <Select
              items={levels.map((level) => ({
                value: level,
                label: thinkingLabel(level),
              }))}
              value={ready.thinking}
              onValueChange={(value) => {
                setChosen({ ...ready, thinking: value as string })
                save.reset()
              }}
            >
              <SelectTrigger aria-label="Thinking">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {levels.map((level) => (
                  <SelectItem key={level} value={level}>
                    {thinkingLabel(level)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {error?.fields.thinking && (
              <FieldError>{error.fields.thinking[0]}</FieldError>
            )}
          </Field>
        )}
        <Field data-invalid={!!error?.fields.name || undefined}>
          <FieldLabel htmlFor="preset-new-name">Name</FieldLabel>
          <Input
            id="preset-new-name"
            value={name}
            placeholder={defaultName}
            onChange={(event) => setName(event.target.value)}
            aria-invalid={!!error?.fields.name || undefined}
          />
          {error?.fields.name && (
            <FieldError>{error.fields.name[0]}</FieldError>
          )}
        </Field>
        {save.isError &&
          !error?.fields.account &&
          !error?.fields.model &&
          !error?.fields.name && (
            <FieldError>{messageOf(save.error)}</FieldError>
          )}
      </FieldGroup>
      <DialogFooter>
        <Button type="submit" loading={save.isPending} disabled={!ready}>
          Save preset
        </Button>
      </DialogFooter>
    </form>
  )
}

// The vendor's name for a model as people say it, without the vendor: "GPT-6 Luna".
function spokenName(name: string) {
  const vendor = name.indexOf(": ")
  return vendor < 0 ? name : name.slice(vendor + 2)
}
