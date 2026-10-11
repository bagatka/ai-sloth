import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"
import { Link, useNavigate } from "@tanstack/react-router"
import {
  ArrowUpIcon,
  BookBookmarkIcon,
  CaretDownIcon,
  HardDrivesIcon,
} from "@phosphor-icons/react"

import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button, buttonVariants } from "@/components/ui/button"
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { SidebarTrigger } from "@/components/ui/sidebar"
import { Skeleton } from "@/components/ui/skeleton"
import { toast } from "@/components/ui/toast"
import { ApiError, answer, api, messageOf, type Schemas } from "./api"
import { chatStream } from "./chat/chat-stream"
import { MessageBox } from "./chat/message-box"
import { useCurrentWorkspace } from "./chat-list"
import { agentLabel, ChooseAgentDialog, type Choice } from "./choose-agent"
import {
  agentAccounts,
  harnesses as harnessesQuery,
  presets as presetsQuery,
  providers as providersQuery,
  repositories as repositoriesQuery,
} from "./queries"

// Starting a chat: what it runs with (its repositories, its agent with the account that pays and its
// model, picked from the person's presets or chosen anew, and, where there's a choice, where its nook
// runs), and its first message. The chat and its nook start as soon as someone writes, so the agent
// is ready by the time they send; until then it's a draft, which the host lets go after a while. The
// browser remembers each workspace's last choices.

type Workspace = Schemas["WorkspaceSummary"]
type Account = Schemas["AgentAccountSummary"]
type Harness = Schemas["HarnessSummary"]
type ChatSummary = Schemas["ChatSummary"]
type Preset = Schemas["PresetSummary"]

type Setup = {
  account: string
  harness: string
  model: string | null
  provider: string
  repositories: readonly string[]
}

export function NewChatPage() {
  const workspace = useCurrentWorkspace()
  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <title>{workspace ? `${workspace.name} – AiSloth` : "AiSloth"}</title>
      <header className="flex h-16 shrink-0 items-center gap-3 px-4 md:hidden">
        <SidebarTrigger className="-ml-1" />
        <span className="truncate text-title">{workspace?.name}</span>
      </header>
      {/* Choices and drafts belong to one workspace. */}
      {workspace && <NewChat key={workspace.id} workspace={workspace} />}
    </div>
  )
}

function NewChat({ workspace }: { workspace: Workspace }) {
  const navigate = useNavigate()
  const accounts = useQuery(agentAccounts(workspace.id))
  const harnesses = useQuery(harnessesQuery)
  const providers = useQuery(providersQuery(workspace.id))
  const repositories = useQuery(repositoriesQuery(workspace.id))
  const presets = useQuery(presetsQuery(workspace.id))
  const [chosen, setChosen] = React.useState<Setup | null>(() =>
    remembered(workspace.id)
  )
  const [draft, setDraft] = React.useState("")
  const [choosing, setChoosing] = React.useState(false)

  const usable = (accounts.data ?? []).filter((account) => !account.needsSignIn)
  const agents = agentsOf(usable, harnesses.data ?? [])
  const offeredPresets = (presets.data ?? []).filter((preset) =>
    runs(agents, preset)
  )
  const places = (providers.data ?? []).filter((place) => place.available)
  const setup = resolve(
    chosen,
    agents,
    offeredPresets,
    places,
    repositories.data ?? []
  )
  const loading =
    accounts.isPending ||
    harnesses.isPending ||
    presets.isPending ||
    providers.isPending ||
    repositories.isPending
  const agentName =
    harnesses.data?.find((harness) => harness.id === setup?.harness)?.name ??
    "the agent"

  // The drafts started for this page, one per setup it was written with, so writing on with the
  // same choices reuses the draft. A draft the host refused to start is forgotten, to try again.
  const drafts = React.useRef(new Map<string, Promise<ChatSummary>>())
  function draftFor(of: Setup): Promise<ChatSummary> {
    const key = JSON.stringify(of)
    let started = drafts.current.get(key)
    if (!started) {
      started = answer(
        api.POST("/workspaces/{workspaceId}/chats", {
          params: { path: { workspaceId: workspace.id } },
          body: {
            account: of.account,
            harness: of.harness,
            model: of.model,
            provider: of.provider,
            repositories: of.repositories.map((repository) => ({
              repository,
            })),
          },
        })
      )
      drafts.current.set(key, started)
      started.catch(() => drafts.current.delete(key))
    }
    return started
  }

  // Starts the chat's nook while there's something to send.
  function prepare(of: Setup | null, text: string) {
    if (of && text.trim()) {
      draftFor(of).catch(() => {
        // Sending says what went wrong.
      })
    }
  }

  function choose(change: Partial<Setup>) {
    if (!setup) {
      return
    }
    const next = { ...setup, ...change }
    setChosen(next)
    prepare(next, draft)
  }

  const send = useMutation({
    mutationFn: async ({ text, of }: { text: string; of: Setup }) => {
      const chat = await draftFor(of)
      try {
        return { chat, sent: await sendFirst(chat.id, text) }
      } catch (error) {
        // The host lets a draft go after a while, and this one went before anything was sent in
        // it: start the chat again, once.
        if (!(error instanceof ApiError && error.status === 404)) {
          throw error
        }
        drafts.current.delete(JSON.stringify(of))
        const again = await draftFor(of)
        return { chat: again, sent: await sendFirst(again.id, text) }
      }
    },
    onSuccess: ({ chat, sent }, { text, of }) => {
      remember(workspace.id, of)
      const stream = chatStream(chat.id)
      stream.taken(stream.addPending(text), sent.id)
      void navigate({ to: "/chats/$chatId", params: { chatId: chat.id } })
    },
    onError: (error) =>
      toast.add({
        type: "error",
        title: "Not sent",
        description: messageOf(error),
      }),
  })

  function submit() {
    const text = draft.trim()
    if (!text || !setup || send.isPending) {
      return
    }
    send.mutate({ text, of: setup })
  }

  const unusable = !loading && !setup
  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-6 px-4 pb-[12vh]">
      <h1 className="text-center text-2xl font-semibold tracking-tight text-balance">
        What should <span className="whitespace-nowrap">{agentName}</span> work
        on?
      </h1>
      <div className="w-full">
        <MessageBox
          value={draft}
          onValueChange={(value) => {
            setDraft(value)
            prepare(setup, value)
          }}
          onSubmit={submit}
          placeholder="Describe the task, or ask a question"
          autoFocus
          controls={
            loading ? (
              <Skeleton className="ml-1 h-4 w-40" />
            ) : setup ? (
              <>
                {(repositories.data ?? []).length === 0 &&
                  workspace.access === "Manage" && (
                    <Link
                      to="/settings"
                      hash="repositories"
                      className={buttonVariants({
                        variant: "ghost",
                        size: "sm",
                      })}
                    >
                      <BookBookmarkIcon />
                      Add a repository
                    </Link>
                  )}
                {(repositories.data ?? []).length > 0 && (
                  <RepositoriesPicker
                    all={repositories.data ?? []}
                    chosen={setup.repositories}
                    onChange={(chosenRepositories) =>
                      choose({ repositories: chosenRepositories })
                    }
                  />
                )}
                <AgentPicker
                  agents={agents}
                  presets={offeredPresets}
                  setup={setup}
                  onChange={(choice) => choose(choice)}
                  onChooseAnother={() => setChoosing(true)}
                />
                <ChooseAgentDialog
                  workspace={workspace}
                  open={choosing}
                  onOpenChange={setChoosing}
                  purpose="use"
                  initial={setup}
                  onChosen={(choice) => choose(choice)}
                />
                {places.length > 1 && (
                  <PlacePicker
                    places={places}
                    chosen={setup.provider}
                    onChange={(provider) => choose({ provider })}
                  />
                )}
              </>
            ) : null
          }
          action={
            <Button
              type="submit"
              size="icon-sm"
              aria-label="Send"
              disabled={!draft.trim() || !setup}
              loading={send.isPending}
            >
              <ArrowUpIcon weight="bold" />
            </Button>
          }
        />
      </div>
      {unusable && (
        <Alert className="max-w-[44rem]">
          {agents.length === 0 ? (
            <>
              <AlertTitle>No agent account yet</AlertTitle>
              <AlertDescription>
                Chats in {workspace.name} need one to pay for the agent's work:
                an API key, or your own plan.
              </AlertDescription>
              <Link
                to="/settings"
                hash="accounts"
                className={buttonVariants({
                  size: "sm",
                  className: "mt-2 w-fit",
                })}
              >
                Add an agent account
              </Link>
            </>
          ) : (
            <>
              <AlertTitle>Nowhere to start a nook</AlertTitle>
              <AlertDescription>
                None of the places {workspace.name}'s nooks run can start one
                right now, such as machines that are offline.
              </AlertDescription>
            </>
          )}
        </Alert>
      )}
    </div>
  )
}

function sendFirst(chatId: string, text: string) {
  return answer(
    api.POST("/chats/{id}/messages", {
      params: { path: { id: chatId } },
      body: { text },
    })
  )
}

function RepositoriesPicker({
  all,
  chosen,
  onChange,
}: {
  all: readonly Schemas["RepositorySummary"][]
  chosen: readonly string[]
  onChange: (chosen: string[]) => void
}) {
  const names = all
    .filter((repository) => chosen.includes(repository.id))
    .map((repository) => repository.name)
  const label =
    names.length === 0
      ? "No repository"
      : names.length <= 2
        ? names.join(", ")
        : `${names.length} repositories`

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={<Button variant="ghost" size="sm" className="min-w-0" />}
      >
        <BookBookmarkIcon />
        <span className="truncate">{label}</span>
        <CaretDownIcon className="text-muted-foreground" />
      </DropdownMenuTrigger>
      <DropdownMenuContent className="w-auto min-w-56">
        <DropdownMenuGroup>
          <DropdownMenuLabel>Repositories in its nook</DropdownMenuLabel>
          {all.map((repository) => (
            <DropdownMenuCheckboxItem
              key={repository.id}
              checked={chosen.includes(repository.id)}
              onCheckedChange={(checked) =>
                onChange(
                  checked
                    ? [...chosen, repository.id]
                    : chosen.filter((id) => id !== repository.id)
                )
              }
            >
              <span className="flex min-w-0 flex-col">
                <span className="truncate">{repository.name}</span>
                <span className="truncate text-xs text-muted-foreground">
                  {repository.fullName}
                </span>
              </span>
            </DropdownMenuCheckboxItem>
          ))}
        </DropdownMenuGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

type Agent = { account: Account; harnesses: readonly Harness[] }

// Whether a choice, such as a preset, can run here: its account is usable, and its harness takes it.
function runs(
  agents: readonly Agent[],
  choice: { account: string; harness: string }
) {
  return agents.some(
    (agent) =>
      agent.account.id === choice.account &&
      agent.harnesses.some((harness) => harness.id === choice.harness)
  )
}

// The agents a chat can run: each account, with the harnesses that take its kind.
function agentsOf(
  accounts: readonly Account[],
  harnesses: readonly Harness[]
): Agent[] {
  return accounts
    .map((account) => ({
      account,
      harnesses: harnesses.filter((harness) =>
        harness.accepts.includes(account.kind)
      ),
    }))
    .filter((agent) => agent.harnesses.length > 0)
}

// The person's presets when they have some, otherwise every agent each account can run on its
// default model; anything else, such as another model, is chosen in a dialog.
function AgentPicker({
  agents,
  presets,
  setup,
  onChange,
  onChooseAnother,
}: {
  agents: readonly Agent[]
  presets: readonly Preset[]
  setup: Setup
  onChange: (choice: Choice) => void
  onChooseAnother: () => void
}) {
  const agent = agents.find((found) => found.account.id === setup.account)
  const harness = agent?.harnesses.find((found) => found.id === setup.harness)
  const preset = presets.find(
    (found) =>
      found.harness === setup.harness &&
      found.account === setup.account &&
      found.model === setup.model
  )
  const accountName = (id: string) =>
    agents.find((found) => found.account.id === id)?.account.name

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={<Button variant="ghost" size="sm" className="min-w-0" />}
      >
        <span className="truncate">
          {preset?.name ?? agentLabel(harness?.name ?? "", setup.model)}
        </span>
        {!preset && agents.length > 1 && (
          <span className="truncate font-normal text-muted-foreground">
            {agent?.account.name}
          </span>
        )}
        <CaretDownIcon className="text-muted-foreground" />
      </DropdownMenuTrigger>
      <DropdownMenuContent className="w-auto min-w-56">
        {presets.length > 0 ? (
          <DropdownMenuGroup>
            <DropdownMenuLabel>Your presets</DropdownMenuLabel>
            <DropdownMenuRadioGroup
              value={preset?.id ?? ""}
              onValueChange={(id: string) => {
                const picked = presets.find((found) => found.id === id)
                if (picked) {
                  onChange(picked)
                }
              }}
            >
              {presets.map((offered) => (
                <DropdownMenuRadioItem
                  key={offered.id}
                  value={offered.id}
                  closeOnClick
                >
                  <span className="truncate">{offered.name}</span>
                  <span className="ml-auto truncate pl-3 text-xs text-muted-foreground">
                    {accountName(offered.account)}
                  </span>
                </DropdownMenuRadioItem>
              ))}
            </DropdownMenuRadioGroup>
          </DropdownMenuGroup>
        ) : (
          <DropdownMenuRadioGroup
            value={setup.model ? "" : `${setup.account}/${setup.harness}`}
            onValueChange={(chosen: string) => {
              const [account, harnessId] = chosen.split("/")
              onChange({ account: account!, harness: harnessId!, model: null })
            }}
          >
            {agents.map(({ account, harnesses }) => (
              <DropdownMenuGroup key={account.id}>
                <DropdownMenuLabel>
                  {account.name}
                  <span className="font-normal">
                    {account.ownerId ? ", your own" : ", the workspace's"}
                  </span>
                </DropdownMenuLabel>
                {harnesses.map((offered) => (
                  <DropdownMenuRadioItem
                    key={offered.id}
                    value={`${account.id}/${offered.id}`}
                    closeOnClick
                  >
                    {offered.name}
                  </DropdownMenuRadioItem>
                ))}
              </DropdownMenuGroup>
            ))}
          </DropdownMenuRadioGroup>
        )}
        <DropdownMenuSeparator />
        <DropdownMenuItem onClick={onChooseAnother}>
          Another agent or model…
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

function PlacePicker({
  places,
  chosen,
  onChange,
}: {
  places: readonly Schemas["ProviderSummary"][]
  chosen: string
  onChange: (provider: string) => void
}) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={<Button variant="ghost" size="sm" className="min-w-0" />}
      >
        <HardDrivesIcon />
        <span className="truncate">
          {places.find((place) => place.id === chosen)?.name}
        </span>
        <CaretDownIcon className="text-muted-foreground" />
      </DropdownMenuTrigger>
      <DropdownMenuContent className="w-auto min-w-48">
        <DropdownMenuGroup>
          <DropdownMenuLabel>Where its nook runs</DropdownMenuLabel>
          <DropdownMenuRadioGroup
            value={chosen}
            onValueChange={(provider: string) => onChange(provider)}
          >
            {places.map((place) => (
              <DropdownMenuRadioItem
                key={place.id}
                value={place.id}
                closeOnClick
              >
                {place.name}
              </DropdownMenuRadioItem>
            ))}
          </DropdownMenuRadioGroup>
        </DropdownMenuGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

// The choices as they can be made now: each one kept while it's still offered, otherwise the first
// preset, or the first agent on its default model. Null when there's no agent to run or nowhere to
// run it.
function resolve(
  chosen: Setup | null,
  agents: readonly Agent[],
  presets: readonly Preset[],
  places: readonly Schemas["ProviderSummary"][],
  repositories: readonly Schemas["RepositorySummary"][]
): Setup | null {
  const place =
    places.find((found) => found.id === chosen?.provider) ?? places[0]
  const kept =
    chosen && runs(agents, chosen)
      ? {
          account: chosen.account,
          harness: chosen.harness,
          model: chosen.model,
        }
      : null
  const first = agents[0]
  const agent =
    kept ??
    presets[0] ??
    (first && {
      account: first.account.id,
      harness: first.harnesses[0]!.id,
      model: null,
    })
  if (!agent || !place) {
    return null
  }
  return {
    account: agent.account,
    harness: agent.harness,
    model: agent.model,
    provider: place.id,
    repositories: (chosen?.repositories ?? []).filter((id) =>
      repositories.some((repository) => repository.id === id)
    ),
  }
}

// Each workspace's last choices, in this browser only; without storage, the first of each.
function remembered(workspaceId: string): Setup | null {
  try {
    const saved = JSON.parse(
      localStorage.getItem(`new-chat:${workspaceId}`) ?? "null"
    ) as Partial<Setup> | null
    return saved &&
      typeof saved.account === "string" &&
      typeof saved.harness === "string" &&
      typeof saved.provider === "string" &&
      Array.isArray(saved.repositories)
      ? { ...(saved as Setup), model: saved.model ?? null }
      : null
  } catch {
    return null
  }
}

function remember(workspaceId: string, setup: Setup) {
  try {
    localStorage.setItem(`new-chat:${workspaceId}`, JSON.stringify(setup))
  } catch {
    // The browser keeps nothing; the next chat starts from the first choices.
  }
}
