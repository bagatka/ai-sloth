import { useQueries, useQuery } from "@tanstack/react-query"
import { BrainIcon, CaretDownIcon } from "@phosphor-icons/react"
import { cn } from "cn"

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
import {
  dollarsLabel,
  fitThinking,
  modelLabel,
  thinkingLabel,
  tokensLabel,
  type Choice,
  type Chosen,
} from "./models"
import { isPlan, metersSummary, nearLimit } from "./plan-usage"
import {
  agentAccounts,
  models as modelsQuery,
  planUsage as planUsageQuery,
} from "./queries"

// What a chat runs on, picked in its message box the way coding agents' own pickers work: one click
// opens every model of every account the agent can run on, grouped by account, with the person's
// presets first; typing narrows it by name or ID, and one click picks. How hard the agent thinks is
// picked beside it. Before a chat starts, its agent (harness) is picked too; once its nook runs one,
// the model, thinking, and account still change, for the next turn. A plan's group says what it has
// used of its limits, so a plan near one is passed over for another account.

type Account = Schemas["AgentAccountSummary"]
type Harness = Schemas["HarnessSummary"]
type Preset = Schemas["PresetSummary"]

type Item = {
  key: string
  label: string
  detail: string | null
  /** What's known of a listed model, shown at the item's end. */
  model?: Schemas["HarnessModel"]
  choice: Chosen | null
}
type Group = {
  value: string
  label: string
  /** What a plan has used of its limits, and whether one is nearly used up. */
  meters?: { summary: string; near: boolean }
  items: Item[]
}

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
  const usages = useQueries({
    queries: accounts.map((account) => ({
      ...planUsageQuery(workspaceId, account.id),
      enabled: isPlan(account),
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
    // Another model keeps how hard the agent thinks, when it takes that level.
    const choiceOf = (model: Schemas["HarnessModel"]): Chosen => ({
      harness: harness.id,
      account: account.id,
      model: model.id,
      thinking: fitThinking(
        value.thinking,
        model.thinkingLevels,
        harness.defaultThinking
      ),
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
          model,
          choice: choiceOf(model),
        })
      }
    }
    const usage = usages[index]
    const meters = usage?.data?.meters ?? []
    // As of when they were read: the picker doesn't tick.
    const now = usage?.dataUpdatedAt ?? 0
    groups.push({
      value: account.id,
      label: `${account.name}${account.ownerId ? ", your own" : ""}`,
      meters:
        meters.length > 0
          ? {
              summary: metersSummary(meters, now),
              near: nearLimit(meters, now).length > 0,
            }
          : undefined,
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
              <ComboboxLabel className="flex items-baseline gap-2">
                <span className="truncate">{group.label}</span>
                {group.meters && (
                  <span
                    className={cn(
                      "ml-auto shrink-0 tabular-nums",
                      group.meters.near && "text-warning"
                    )}
                  >
                    {group.meters.summary}
                  </span>
                )}
              </ComboboxLabel>
              <ComboboxCollection>
                {(item: Item) => (
                  <ComboboxItem
                    key={item.key}
                    value={item}
                    disabled={!item.choice}
                  >
                    <span className="flex min-w-0 flex-1 flex-col">
                      <span className="truncate">{item.label}</span>
                      {item.detail && (
                        <span className="truncate text-xs text-muted-foreground">
                          {item.detail}
                        </span>
                      )}
                    </span>
                    {item.model && <ModelMeta facts={item.model.facts} />}
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

// A model's context and price per million input and output tokens, at the end of its item. A plan's
// price is an estimate at the vendor's API prices.
function ModelMeta({ facts }: { facts: Schemas["ModelFacts"] }) {
  const price = facts.price
  if (!facts.contextWindow && !price) {
    return null
  }
  return (
    <span className="flex shrink-0 flex-col items-end text-xs text-muted-foreground tabular-nums">
      {facts.contextWindow && (
        <span>{tokensLabel(facts.contextWindow)} context</span>
      )}
      {price && (
        <span
          title={`Per million tokens: ${dollarsLabel(price.input)} in, ${dollarsLabel(price.output)} out${price.estimate ? ", at the API's prices" : ""}`}
        >
          {dollarsLabel(price.input, price.estimate)} /{" "}
          {dollarsLabel(price.output)}
        </span>
      )}
    </span>
  )
}

/**
 * The chat's model as its account lists it, with its thinking levels and facts; undefined while the
 * list loads or when it lacks the model. The pickers' list, so it costs no other call.
 */
export function useListedModel(
  workspaceId: string,
  harness: string,
  account: string,
  model: string | null
): Schemas["HarnessModel"] | undefined {
  const listed = useQuery({
    ...modelsQuery(workspaceId, harness, account),
    enabled: !!model,
  })
  return listed.data?.find((found) => found.id === model)
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

/**
 * How hard the agent thinks, among the levels the chat's model takes; a model that doesn't reason says
 * so in its place.
 */
export function ThinkingPicker({
  levels,
  value,
  onChange,
}: {
  levels: readonly string[]
  value: string
  onChange: (thinking: string) => void
}) {
  if (levels.length === 0) {
    return (
      <span
        className="flex h-8 items-center gap-1.5 px-2.5 text-sm text-muted-foreground"
        title="This model doesn't reason"
      >
        <BrainIcon />
        No thinking
      </span>
    )
  }
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
