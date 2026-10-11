import { useQueries, useQuery } from "@tanstack/react-query"
import { BrainIcon, CaretDownIcon } from "@phosphor-icons/react"

import { Button } from "@/components/ui/button"
import {
  Combobox,
  ComboboxCollection,
  ComboboxContent,
  ComboboxEmpty,
  ComboboxGroup,
  ComboboxInput,
  ComboboxItem,
  ComboboxLabel,
  ComboboxList,
  ComboboxTrigger,
} from "@/components/ui/combobox"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { type Schemas } from "./api"
import { modelLabel, thinkingLabel, type Choice, type Chosen } from "./models"
import { agentAccounts, models as modelsQuery } from "./queries"

// What a chat runs on, picked in its message box the way coding agents' own pickers work: one click
// opens every model of every account the agent can run on, grouped by account, with the person's
// presets first; typing narrows it by name or ID, and one click picks. How hard the agent thinks is
// picked beside it. Before a chat starts, its agent (harness) is picked too; once its nook runs one,
// the model, thinking, and account still change, for the next turn.

type Account = Schemas["AgentAccountSummary"]
type Harness = Schemas["HarnessSummary"]
type Preset = Schemas["PresetSummary"]

type Item = {
  key: string
  label: string
  detail: string | null
  choice: Chosen | null
}
type Group = { value: string; label: string; items: Item[] }

/** The accounts a harness can run on, in the order the host lists them. */
export function accountsFor(
  accounts: readonly Account[],
  harness: Harness
): Account[] {
  return accounts.filter(
    (account) => !account.needsSignIn && harness.accepts.includes(account.kind)
  )
}

export function ModelPicker({
  workspaceId,
  harness,
  value,
  owner,
  presets,
  onChange,
  variant = "ghost",
}: {
  workspaceId: string
  harness: Harness
  value: Choice
  /** Only the accounts of this owner, null for the workspace's; any, when not given. */
  owner?: string | null
  presets: readonly Preset[]
  onChange: (choice: Chosen) => void
  /** Ghost in a message box; outline in a form. */
  variant?: "ghost" | "outline"
}) {
  const all = useQuery(agentAccounts(workspaceId)).data ?? []
  const accounts = accountsFor(all, harness).filter(
    (account) => owner === undefined || (account.ownerId ?? null) === owner
  )
  const lists = useQueries({
    queries: accounts.map((account) => ({
      ...modelsQuery(workspaceId, harness.id, account.id),
    })),
  })

  const groups: Group[] = []
  const quick = presets.filter(
    (preset) =>
      preset.harness === harness.id &&
      accounts.some((account) => account.id === preset.account)
  )
  if (quick.length > 0) {
    groups.push({
      value: "presets",
      label: "Presets",
      items: quick.map((preset) => ({
        key: `preset:${preset.id}`,
        label: preset.name,
        detail:
          accounts.find((account) => account.id === preset.account)?.name ??
          null,
        choice: {
          harness: preset.harness,
          account: preset.account,
          model: preset.model,
          thinking: preset.thinking,
        },
      })),
    })
  }
  accounts.forEach((account, index) => {
    const list = lists[index]
    // Another model keeps how hard the agent thinks.
    const choiceOf = (model: string): Chosen => ({
      harness: harness.id,
      account: account.id,
      model,
      thinking: value.thinking ?? harness.defaultThinking,
    })
    const items: Item[] = []
    if (list?.isPending) {
      items.push({
        key: `${account.id}:loading`,
        label: "Listing models…",
        detail: null,
        choice: null,
      })
    } else if (list?.isError) {
      items.push({
        key: `${account.id}:error`,
        label: "The models couldn't be listed",
        detail: null,
        choice: null,
      })
    } else {
      for (const model of list?.data ?? []) {
        items.push({
          key: `${account.id}:${model.id}`,
          label: model.name,
          detail: model.name === model.id ? null : model.id,
          choice: choiceOf(model.id),
        })
      }
    }
    groups.push({
      value: account.id,
      label: `${account.name}${account.ownerId ? ", your own" : ""}`,
      items,
    })
  })

  const selected =
    groups
      .flatMap((group) => group.items)
      .find(
        (item) =>
          !item.key.startsWith("preset:") &&
          item.choice?.account === value.account &&
          item.choice.model === value.model
      ) ?? null
  const account = accounts.find((found) => found.id === value.account)

  return (
    <Combobox
      items={groups}
      value={selected}
      onValueChange={(item: Item | null) =>
        item?.choice && onChange(item.choice)
      }
      itemToStringLabel={(item: Item) => item.label}
      isItemEqualToValue={(item: Item, other: Item) => item.key === other.key}
      filter={(item: Item, query: string) => {
        const text = query.trim().toLowerCase()
        return (
          !text ||
          item.label.toLowerCase().includes(text) ||
          (item.detail ?? "").toLowerCase().includes(text)
        )
      }}
    >
      <ComboboxTrigger
        render={
          <Button
            variant={variant}
            size="sm"
            className={
              variant === "outline" ? "w-full justify-between" : "min-w-0"
            }
            aria-label="Model"
          />
        }
      >
        <span className="flex min-w-0 items-baseline gap-1.5">
          <span className="truncate">
            {value.model ? modelLabel(value.model) : "Choose a model"}
          </span>
          {accounts.length > 1 && account && (
            <span className="truncate font-normal text-muted-foreground">
              {account.name}
            </span>
          )}
        </span>
      </ComboboxTrigger>
      <ComboboxContent className="w-80 min-w-80">
        <ComboboxInput
          placeholder="Search models"
          aria-label="Search models"
          showTrigger={false}
        />
        <ComboboxEmpty>No model by that name.</ComboboxEmpty>
        <ComboboxList>
          {(group: Group) => (
            <ComboboxGroup key={group.value} items={group.items}>
              <ComboboxLabel>{group.label}</ComboboxLabel>
              <ComboboxCollection>
                {(item: Item) => (
                  <ComboboxItem
                    key={item.key}
                    value={item}
                    disabled={!item.choice}
                  >
                    <span className="flex min-w-0 flex-col">
                      <span className="truncate">{item.label}</span>
                      {item.detail && (
                        <span className="truncate text-xs text-muted-foreground">
                          {item.detail}
                        </span>
                      )}
                    </span>
                  </ComboboxItem>
                )}
              </ComboboxCollection>
            </ComboboxGroup>
          )}
        </ComboboxList>
      </ComboboxContent>
    </Combobox>
  )
}

/** The agent a new chat runs, among those some account can run; a chat's nook keeps it after. */
export function HarnessPicker({
  harnesses,
  value,
  onChange,
}: {
  harnesses: readonly Harness[]
  value: string
  onChange: (harness: string) => void
}) {
  const chosen = harnesses.find((harness) => harness.id === value)
  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={
          <Button
            variant="ghost"
            size="sm"
            className="min-w-0"
            aria-label="Agent"
          />
        }
      >
        <span className="truncate">{chosen?.name}</span>
        <CaretDownIcon className="text-muted-foreground" />
      </DropdownMenuTrigger>
      <DropdownMenuContent className="w-auto min-w-44">
        <DropdownMenuGroup>
          <DropdownMenuLabel>Agent</DropdownMenuLabel>
          <DropdownMenuRadioGroup
            value={value}
            onValueChange={(harness: string) => onChange(harness)}
          >
            {harnesses.map((harness) => (
              <DropdownMenuRadioItem
                key={harness.id}
                value={harness.id}
                closeOnClick
              >
                {harness.name}
              </DropdownMenuRadioItem>
            ))}
          </DropdownMenuRadioGroup>
        </DropdownMenuGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

/** How hard the agent thinks, among its harness's levels. */
export function ThinkingPicker({
  levels,
  value,
  onChange,
}: {
  levels: readonly string[]
  value: string
  onChange: (thinking: string) => void
}) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={
          <Button
            variant="ghost"
            size="sm"
            className="min-w-0"
            aria-label="Thinking"
          />
        }
      >
        <BrainIcon />
        <span className="truncate">{thinkingLabel(value)}</span>
        <CaretDownIcon className="text-muted-foreground" />
      </DropdownMenuTrigger>
      <DropdownMenuContent className="w-auto min-w-44">
        <DropdownMenuGroup>
          <DropdownMenuLabel>Thinking</DropdownMenuLabel>
          <DropdownMenuRadioGroup
            value={value}
            onValueChange={(level: string) => onChange(level)}
          >
            {levels.map((level) => (
              <DropdownMenuRadioItem key={level} value={level} closeOnClick>
                {thinkingLabel(level)}
              </DropdownMenuRadioItem>
            ))}
          </DropdownMenuRadioGroup>
        </DropdownMenuGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
