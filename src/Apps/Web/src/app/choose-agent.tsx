import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Combobox,
  ComboboxContent,
  ComboboxEmpty,
  ComboboxInput,
  ComboboxItem,
  ComboboxList,
} from "@/components/ui/combobox"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
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
import { ApiError, answer, api, messageOf, type Schemas } from "./api"
import {
  agentAccounts,
  harnesses as harnessesQuery,
  models as modelsQuery,
  presets as presetsQuery,
} from "./queries"

// Choosing what a chat runs with: an agent (its harness), the account that pays, and the model, from
// the account's vendor's catalog, searched by name. People keep the choices they use most as presets,
// which a new chat offers first; this is where a preset is made, from Settings or a new chat.

type Workspace = Schemas["WorkspaceSummary"]
type Account = Schemas["AgentAccountSummary"]
type Harness = Schemas["HarnessSummary"]
type Model = Schemas["AgentModel"]

export type Choice = { harness: string; account: string; model: string | null }

/** A model as chats and presets show it: its ID without the vendor, such as gpt-6-luna. */
export function modelLabel(model: string) {
  return model.slice(model.lastIndexOf("/") + 1)
}

/** What a chat runs as one line, such as "pi · gpt-6-luna". */
export function agentLabel(harnessName: string, model: string | null) {
  return model ? `${harnessName} · ${modelLabel(model)}` : harnessName
}

// The vendor's name for a model as people say it, without the vendor: "GPT-6 Luna".
function spokenName(model: Model) {
  const vendor = model.name.indexOf(": ")
  return vendor < 0 ? model.name : model.name.slice(vendor + 2)
}

export function ChooseAgentDialog({
  workspace,
  open,
  onOpenChange,
  purpose,
  initial,
  onChosen,
}: {
  workspace: Workspace
  open: boolean
  onOpenChange: (open: boolean) => void
  /** "save" makes a preset; "use" chooses for a new chat, and saves it as a preset if asked. */
  purpose: "save" | "use"
  initial?: Choice
  onChosen?: (choice: Choice) => void
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        {/* A new form each time the dialog opens. */}
        {open && (
          <ChoiceForm
            workspace={workspace}
            purpose={purpose}
            initial={initial}
            onDone={(choice) => {
              onOpenChange(false)
              onChosen?.(choice)
            }}
          />
        )}
      </DialogContent>
    </Dialog>
  )
}

function ChoiceForm({
  workspace,
  purpose,
  initial,
  onDone,
}: {
  workspace: Workspace
  purpose: "save" | "use"
  initial?: Choice
  onDone: (choice: Choice) => void
}) {
  const queryClient = useQueryClient()
  const accounts = (useQuery(agentAccounts(workspace.id)).data ?? []).filter(
    (account) => !account.needsSignIn
  )
  const harnesses = (useQuery(harnessesQuery).data ?? []).filter((harness) =>
    accounts.some((account) => harness.accepts.includes(account.kind))
  )
  const [harnessId, setHarnessId] = React.useState(initial?.harness)
  const [accountId, setAccountId] = React.useState(initial?.account)
  const [model, setModel] = React.useState<string | null>(
    initial?.model ?? null
  )
  const [name, setName] = React.useState("")
  const [save, setSave] = React.useState(true)

  // Each choice holds while it's still offered, otherwise the first that is.
  const harness =
    harnesses.find((found) => found.id === harnessId) ?? harnesses[0]
  const offered = accounts.filter(
    (account) => harness && harness.accepts.includes(account.kind)
  )
  const account = offered.find((found) => found.id === accountId) ?? offered[0]
  const models = useQuery({
    ...modelsQuery(workspace.id, harness?.id ?? "", account?.id ?? ""),
    enabled: !!harness && !!account,
  })
  const catalog = models.data ?? []
  const chosenModel = catalog.find((found) => found.id === model) ?? null
  const choice: Choice | null =
    harness && account
      ? {
          harness: harness.id,
          account: account.id,
          model: chosenModel?.id ?? null,
        }
      : null
  const defaultName = harness
    ? chosenModel
      ? `${harness.name} · ${spokenName(chosenModel)}`
      : harness.name
    : ""

  const saving = useMutation({
    mutationFn: (of: Choice) =>
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
      if (purpose === "save") {
        toast.add({ type: "success", title: `Saved ${preset.name}` })
      }
    },
  })
  const error = saving.error instanceof ApiError ? saving.error : null

  async function submit() {
    if (!choice || saving.isPending) {
      return
    }
    if (purpose === "save" || save) {
      try {
        await saving.mutateAsync(choice)
      } catch (failed) {
        // Choosing again what was saved before just uses it.
        const known =
          failed instanceof ApiError && failed.code === "chats.preset_exists"
        if (purpose === "save" || !known) {
          return
        }
      }
    }
    onDone(choice)
  }

  if (accounts.length === 0 || !harness || !account) {
    return (
      <DialogHeader>
        <DialogTitle>
          {purpose === "save" ? "Add a preset" : "Choose an agent"}
        </DialogTitle>
        <DialogDescription>
          Add an agent account first, in Settings: an API key, or your own plan.
        </DialogDescription>
      </DialogHeader>
    )
  }

  return (
    <form
      className="contents"
      onSubmit={(event) => {
        event.preventDefault()
        void submit()
      }}
    >
      <DialogHeader>
        <DialogTitle>
          {purpose === "save" ? "Add a preset" : "Choose an agent"}
        </DialogTitle>
        <DialogDescription>
          The agent, the account that pays for its work, and its model. Presets
          are yours, to pick from when you start a chat.
        </DialogDescription>
      </DialogHeader>
      <FieldGroup>
        <Field>
          <FieldLabel>Agent</FieldLabel>
          <Picker
            label="Agent"
            items={harnesses.map((found: Harness) => ({
              value: found.id,
              label: found.name,
            }))}
            value={harness.id}
            onChange={(value) => {
              setHarnessId(value)
              setModel(null)
              saving.reset()
            }}
          />
        </Field>
        <Field data-invalid={!!error?.fields.account || undefined}>
          <FieldLabel>Paid by</FieldLabel>
          <Picker
            label="Paid by"
            items={offered.map((found: Account) => ({
              value: found.id,
              label: `${found.name}, ${found.ownerId ? "your own" : `${workspace.name}'s`}`,
            }))}
            value={account.id}
            onChange={(value) => {
              setAccountId(value)
              setModel(null)
              saving.reset()
            }}
          />
          {error?.fields.account && (
            <FieldError>{error.fields.account[0]}</FieldError>
          )}
        </Field>
        <Field data-invalid={!!error?.fields.model || undefined}>
          <FieldLabel htmlFor="choice-model">Model</FieldLabel>
          {models.isPending ? (
            <Skeleton className="h-8 w-full" />
          ) : models.isError ? (
            <FieldDescription>
              The models couldn't be listed: {messageOf(models.error)}. It runs
              on {harness.name}'s default model.
            </FieldDescription>
          ) : catalog.length === 0 ? (
            <FieldDescription>
              {harness.name} runs on its own default model with this account.
            </FieldDescription>
          ) : (
            <>
              <ModelPicker
                catalog={catalog}
                value={chosenModel}
                onChange={(picked) => {
                  setModel(picked?.id ?? null)
                  saving.reset()
                }}
              />
              <FieldDescription>
                Search by name, or leave it empty for {harness.name}'s default.
              </FieldDescription>
            </>
          )}
          {error?.fields.model && (
            <FieldError>{error.fields.model[0]}</FieldError>
          )}
        </Field>
        {purpose === "save" ? (
          <Field data-invalid={!!error?.fields.name || undefined}>
            <FieldLabel htmlFor="choice-name">Name</FieldLabel>
            <Input
              id="choice-name"
              value={name}
              placeholder={defaultName}
              onChange={(event) => setName(event.target.value)}
              aria-invalid={!!error?.fields.name || undefined}
            />
            {error?.fields.name && (
              <FieldError>{error.fields.name[0]}</FieldError>
            )}
          </Field>
        ) : (
          <Field orientation="horizontal">
            <Checkbox
              id="choice-save"
              checked={save}
              onCheckedChange={(checked) => setSave(checked)}
            />
            <FieldLabel htmlFor="choice-save" className="font-normal">
              Save as a preset
            </FieldLabel>
          </Field>
        )}
        {saving.isError &&
          !error?.fields.account &&
          !error?.fields.model &&
          !error?.fields.name && (
            <FieldError>{messageOf(saving.error)}</FieldError>
          )}
      </FieldGroup>
      <DialogFooter>
        <Button type="submit" loading={saving.isPending}>
          {purpose === "save" ? "Save preset" : "Use"}
        </Button>
      </DialogFooter>
    </form>
  )
}

function Picker({
  label,
  items,
  value,
  onChange,
}: {
  label: string
  items: { value: string; label: string }[]
  value: string
  onChange: (value: string) => void
}) {
  return (
    <Select
      items={items}
      value={value}
      onValueChange={(chosen) => chosen && onChange(chosen as string)}
    >
      <SelectTrigger aria-label={label}>
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {items.map((item) => (
          <SelectItem key={item.value} value={item.value}>
            {item.label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

// A catalog of hundreds, searched by the vendor's name for each.
function ModelPicker({
  catalog,
  value,
  onChange,
}: {
  catalog: readonly Model[]
  value: Model | null
  onChange: (model: Model | null) => void
}) {
  return (
    <Combobox
      items={catalog}
      value={value}
      onValueChange={(picked) => onChange(picked)}
      itemToStringLabel={(model: Model) => model.name}
      isItemEqualToValue={(model: Model, other: Model) => model.id === other.id}
    >
      <ComboboxInput
        id="choice-model"
        placeholder="Default model"
        aria-label="Model"
        showClear
      />
      <ComboboxContent>
        <ComboboxEmpty>No model by that name.</ComboboxEmpty>
        <ComboboxList>
          {(model: Model) => (
            <ComboboxItem key={model.id} value={model}>
              <span className="flex min-w-0 flex-col">
                <span className="truncate">{model.name}</span>
                <span className="truncate text-xs text-muted-foreground">
                  {model.id}
                </span>
              </span>
            </ComboboxItem>
          )}
        </ComboboxList>
      </ComboboxContent>
    </Combobox>
  )
}
