import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { ArrowSquareOutIcon, PlusIcon } from "@phosphor-icons/react"
import { cn } from "cn"

import { Alert, AlertDescription } from "@/components/ui/alert"
import { Button, buttonVariants } from "@/components/ui/button"
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
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group"
import { ApiError, answer, api, messageOf, type Schemas } from "../api"
import { metersSummary, nearLimit, usePlanUsage } from "../plan-usage"
import { accountKinds, agentAccounts, me } from "../queries"
import { aborted, CodeSteps, waitForCode } from "./code-sign-in"
import { NoRows, RemoveButton, Row, Rows, Section } from "./section"

// Agent accounts: what pays for the agents' work. A workspace's account is for everyone who works
// there; someone's own is for their chats alone. API keys are added with their secret, plans by
// signing in at their vendor: a ChatGPT plan with a code entered at OpenAI, as Codex does, a Copilot
// plan with a code entered at GitHub, and a Claude plan with the code Claude's page shows, as Claude
// Code does.

type Workspace = Schemas["WorkspaceSummary"]
type Kind = Schemas["AgentAccountKind"]

// How each kind is named and asked for, as sloth does: a plan after the product people subscribe
// to, an API key after the API it calls.
const kindNames: Record<
  Kind,
  {
    name: string
    description: string
    secret: string | null
    /** Another endpoint that speaks its API, for the kinds that take one. */
    endpoint: string | null
  }
> = {
  AnthropicApiKey: {
    name: "Anthropic API key",
    description: "Anthropic's API, or any endpoint that speaks it",
    secret: "API key",
    endpoint: "https://gateway.example.com",
  },
  OpenAIApiKey: {
    name: "OpenAI API key",
    description: "OpenAI's API, or any endpoint that speaks it",
    secret: "API key",
    endpoint: "https://gateway.example.com/v1",
  },
  OpenRouterApiKey: {
    name: "OpenRouter API key",
    description: "Many vendors' models with one key, for Claude Code and pi",
    secret: "API key",
    endpoint: null,
  },
  ChatGptPlan: {
    name: "ChatGPT plan",
    description: "ChatGPT Plus or Pro",
    secret: null,
    endpoint: null,
  },
  ClaudePlan: {
    name: "Claude plan",
    description: "Claude Pro or Max",
    secret: null,
    endpoint: null,
  },
  CopilotPlan: {
    name: "Copilot plan",
    description: "GitHub Copilot",
    secret: null,
    endpoint: null,
  },
}

// The plans added with a code entered at their vendor, and where.
const codeVendors: Partial<Record<Kind, string>> = {
  CopilotPlan: "GitHub",
  ChatGptPlan: "OpenAI",
}

// Own accounts show in every workspace, and presets where their account works, so any account
// change reads them all again.
const isAccountList = (query: { queryKey: readonly unknown[] }) =>
  query.queryKey[0] === "workspaces" &&
  (query.queryKey[2] === "agent-accounts" || query.queryKey[2] === "presets")

export function AccountsSection({ workspace }: { workspace: Workspace }) {
  const queryClient = useQueryClient()
  const person = useQuery(me).data
  const list = useQuery(agentAccounts(workspace.id))
  const manager = workspace.access === "Manage"
  const remove = useMutation({
    mutationFn: (id: string) =>
      answer(api.DELETE("/agent-accounts/{id}", { params: { path: { id } } })),
    onSuccess: () =>
      queryClient.invalidateQueries({ predicate: isAccountList }),
    onError: (error) =>
      toast.add({
        type: "error",
        title: "Not removed",
        description: messageOf(error),
      }),
  })

  return (
    <Section
      id="accounts"
      title="Agent accounts"
      description={`What pays for the agents' work. ${workspace.name}'s accounts are for everyone who works there; your own are for your chats.`}
      action={<AddAccount workspace={workspace} manager={manager} />}
    >
      <Rows label="Agent accounts">
        {list.isPending ? (
          <NoRows>
            <Skeleton className="h-4 w-48" />
          </NoRows>
        ) : (list.data ?? []).length === 0 ? (
          <NoRows>
            None yet. Chats need one: add an API key, or your own plan.
          </NoRows>
        ) : (
          (list.data ?? []).map((account) => {
            const own = account.ownerId === person?.id
            return (
              <Row
                key={account.id}
                title={account.name}
                detail={[
                  kindNames[account.kind].name,
                  own ? "yours" : `${workspace.name}'s`,
                  account.needsSignIn ? "sign in again to use it" : null,
                ]
                  .filter(Boolean)
                  .join(", ")}
              >
                <PlanMeters workspaceId={workspace.id} account={account} />
                {(own || manager) && (
                  <RemoveButton
                    what={account.name}
                    consequence="Agents running on it stop reaching their model at once, and chats can't start on it."
                    pending={
                      remove.isPending && remove.variables === account.id
                    }
                    onRemove={() => remove.mutate(account.id)}
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

// What a plan has used of its limits, at its row's end, as of when it was read.
function PlanMeters({
  workspaceId,
  account,
}: {
  workspaceId: string
  account: Schemas["AgentAccountSummary"]
}) {
  const usage = usePlanUsage(workspaceId, account)
  const meters = usage.data?.meters ?? []
  if (meters.length === 0) {
    return null
  }
  return (
    <span
      className={cn(
        "hidden text-label text-muted-foreground tabular-nums sm:inline",
        nearLimit(meters, usage.dataUpdatedAt).length > 0 && "text-warning"
      )}
    >
      {metersSummary(meters, usage.dataUpdatedAt)}
    </span>
  )
}

function AddAccount({
  workspace,
  manager,
}: {
  workspace: Workspace
  manager: boolean
}) {
  const [open, setOpen] = React.useState(false)
  // Closing the dialog stops waiting for a code's approval.
  const closed = React.useRef(new AbortController())
  return (
    <Dialog
      open={open}
      onOpenChange={(opening) => {
        setOpen(opening)
        if (opening) {
          closed.current = new AbortController()
        } else {
          closed.current.abort()
        }
      }}
    >
      <DialogTrigger render={<Button variant="outline" size="sm" />}>
        <PlusIcon data-icon="inline-start" />
        Add
      </DialogTrigger>
      <DialogContent>
        {/* A new form each time the dialog opens. */}
        {open && (
          <AccountForm
            workspace={workspace}
            manager={manager}
            closed={closed}
            onAdded={() => setOpen(false)}
          />
        )}
      </DialogContent>
    </Dialog>
  )
}

function AccountForm({
  workspace,
  manager,
  closed,
  onAdded,
}: {
  workspace: Workspace
  manager: boolean
  /** Aborts when the dialog closes. */
  closed: React.RefObject<AbortController>
  onAdded: () => void
}) {
  const queryClient = useQueryClient()
  const offered = (useQuery(accountKinds).data ?? []).filter(
    (kind) => kind.allowed
  )
  const [kind, setKind] = React.useState<Kind | null>(null)
  const chosen = offered.find((found) => found.kind === kind) ?? offered[0]
  const names = chosen ? kindNames[chosen.kind] : null
  // A plan is someone's own; managers may add API keys for everyone.
  const [everyone, setEveryone] = React.useState(manager)
  const shared = manager && !chosen?.personalOnly && everyone
  const [name, setName] = React.useState("")
  const [secret, setSecret] = React.useState("")
  const [endpoint, setEndpoint] = React.useState("")

  const added = (account: Schemas["AgentAccountSummary"]) => {
    void queryClient.invalidateQueries({ predicate: isAccountList })
    toast.add({ type: "success", title: `Added ${account.name}` })
    onAdded()
  }
  const add = useMutation({
    mutationFn: () => {
      const body = {
        kind: chosen!.kind,
        name: name.trim() || names!.name,
        secret,
        endpoint: endpoint.trim() || null,
      }
      return shared
        ? answer(
            api.POST("/workspaces/{workspaceId}/agent-accounts", {
              params: { path: { workspaceId: workspace.id } },
              body,
            })
          )
        : answer(api.POST("/agent-accounts", { body }))
    },
    onSuccess: added,
  })
  const error = add.error instanceof ApiError ? add.error : null

  if (!chosen || !names) {
    return (
      <DialogHeader>
        <DialogTitle>Add an agent account</DialogTitle>
        <Skeleton className="h-4 w-48" />
      </DialogHeader>
    )
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>Add an agent account</DialogTitle>
        <DialogDescription>
          {chosen.kind === "CopilotPlan"
            ? "Its token is kept on the host, sealed, and reaches only your Copilot; agents use it in their nooks."
            : chosen.kind === "ChatGptPlan"
              ? "Codex's sign-in is kept on the host, sealed, and renewed there; Codex reaches ChatGPT through the host, so no nook holds it."
              : chosen.kind === "ClaudePlan"
                ? "Claude Code's sign-in is kept on the host, sealed, and renewed there; Claude Code reaches Claude through the host, so no nook holds it."
                : "It's kept on the host, sealed, and never shown again; agents reach their model through the host, so no nook holds it."}
        </DialogDescription>
      </DialogHeader>
      <FieldGroup>
        <Field>
          <FieldLabel>Kind</FieldLabel>
          <Select
            items={offered.map((found) => ({
              value: found.kind,
              label: kindNames[found.kind].name,
            }))}
            value={chosen.kind}
            onValueChange={(value) => {
              setKind(value as Kind)
              add.reset()
            }}
          >
            <SelectTrigger aria-label="Kind">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {offered.map((found) => (
                <SelectItem key={found.kind} value={found.kind}>
                  {kindNames[found.kind].name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <FieldDescription>{names.description}</FieldDescription>
        </Field>
        {manager && !chosen.personalOnly && (
          <Field>
            <FieldLabel>For</FieldLabel>
            <ToggleGroup
              value={[everyone ? "everyone" : "you"]}
              onValueChange={(value) =>
                value[0] && setEveryone(value[0] === "everyone")
              }
              variant="outline"
              spacing={0}
              size="sm"
              className="w-fit"
            >
              <ToggleGroupItem value="everyone">
                Everyone in {workspace.name}
              </ToggleGroupItem>
              <ToggleGroupItem value="you">Just you</ToggleGroupItem>
            </ToggleGroup>
          </Field>
        )}
        <Field data-invalid={!!error?.fields.name || undefined}>
          <FieldLabel htmlFor="account-name">Name</FieldLabel>
          <Input
            id="account-name"
            value={name}
            placeholder={names.name}
            onChange={(event) => setName(event.target.value)}
            aria-invalid={!!error?.fields.name || undefined}
          />
          {error?.fields.name && (
            <FieldError>{error.fields.name[0]}</FieldError>
          )}
        </Field>
      </FieldGroup>
      {codeVendors[chosen.kind] ? (
        <CodeSignIn
          kind={chosen.kind}
          vendor={codeVendors[chosen.kind]!}
          name={name.trim() || names.name}
          closed={closed}
          onAdded={added}
        />
      ) : chosen.addedBySignIn ? (
        <ClaudeSignIn name={name.trim() || names.name} onAdded={added} />
      ) : (
        <form
          className="contents"
          onSubmit={(event) => {
            event.preventDefault()
            if (secret.trim()) {
              add.mutate()
            }
          }}
        >
          <FieldGroup>
            <Field data-invalid={!!error?.fields.secret || undefined}>
              <FieldLabel htmlFor="account-secret">{names.secret}</FieldLabel>
              <Input
                id="account-secret"
                type="password"
                autoComplete="off"
                value={secret}
                onChange={(event) => setSecret(event.target.value)}
                aria-invalid={!!error?.fields.secret || undefined}
              />
              {error?.fields.secret && (
                <FieldError>{error.fields.secret[0]}</FieldError>
              )}
            </Field>
            {names.endpoint && (
              <Field data-invalid={!!error?.fields.endpoint || undefined}>
                <FieldLabel htmlFor="account-endpoint">
                  Endpoint, if not the vendor's own
                </FieldLabel>
                <Input
                  id="account-endpoint"
                  type="url"
                  value={endpoint}
                  placeholder={names.endpoint}
                  onChange={(event) => setEndpoint(event.target.value)}
                  aria-invalid={!!error?.fields.endpoint || undefined}
                />
                {error?.fields.endpoint ? (
                  <FieldError>{error.fields.endpoint[0]}</FieldError>
                ) : (
                  <FieldDescription>
                    Any service that speaks the same API.
                  </FieldDescription>
                )}
              </Field>
            )}
            {add.isError &&
              !error?.fields.name &&
              !error?.fields.secret &&
              !error?.fields.endpoint && (
                <Alert variant="destructive">
                  <AlertDescription>{messageOf(add.error)}</AlertDescription>
                </Alert>
              )}
          </FieldGroup>
          <DialogFooter>
            <Button
              type="submit"
              loading={add.isPending}
              disabled={!secret.trim()}
            >
              Add account
            </Button>
          </DialogFooter>
        </form>
      )}
    </>
  )
}

// Signing in as Claude Code does: the host makes the sign-in address; the person approves there, and
// Claude's page shows a code, which they paste back for the host to finish adding the plan.
function ClaudeSignIn({
  name,
  onAdded,
}: {
  name: string
  onAdded: (account: Schemas["AgentAccountSummary"]) => void
}) {
  const [code, setCode] = React.useState("")
  const start = useMutation({
    mutationFn: () =>
      answer(
        api.POST("/agent-accounts/sign-ins", {
          body: { kind: "ClaudePlan", name },
        })
      ),
  })
  const complete = useMutation({
    mutationFn: (id: string) =>
      answer(
        api.POST("/agent-accounts/sign-ins/{id}/complete", {
          params: { path: { id } },
          body: { code: code.trim() },
        })
      ),
    onSuccess: (progress) => progress.account && onAdded(progress.account),
  })

  if (!start.data) {
    return (
      <>
        {start.isError && (
          <Alert variant="destructive">
            <AlertDescription>{messageOf(start.error)}</AlertDescription>
          </Alert>
        )}
        <DialogFooter>
          <Button loading={start.isPending} onClick={() => start.mutate()}>
            Sign in with Claude
          </Button>
        </DialogFooter>
      </>
    )
  }

  return (
    <form
      className="contents"
      onSubmit={(event) => {
        event.preventDefault()
        if (code.trim()) {
          complete.mutate(start.data.id)
        }
      }}
    >
      <FieldGroup>
        <a
          href={start.data.url}
          target="_blank"
          rel="noopener noreferrer"
          className={buttonVariants({ variant: "outline", className: "w-fit" })}
        >
          Open Claude to sign in
          <ArrowSquareOutIcon data-icon="inline-end" />
        </a>
        <Field data-invalid={complete.isError || undefined}>
          <FieldLabel htmlFor="claude-code">The code Claude shows</FieldLabel>
          <Input
            id="claude-code"
            value={code}
            autoComplete="off"
            onChange={(event) => setCode(event.target.value)}
            aria-invalid={complete.isError || undefined}
          />
          {complete.isError ? (
            <FieldError>{messageOf(complete.error)}</FieldError>
          ) : (
            <FieldDescription>
              After you approve, Claude shows a code. Copy it and paste it
              here, within ten minutes.
            </FieldDescription>
          )}
        </Field>
      </FieldGroup>
      <DialogFooter>
        {complete.isError && (
          // A sign-in that failed or expired can't be completed; a new one can.
          <Button
            variant="outline"
            onClick={() => {
              complete.reset()
              start.reset()
              setCode("")
            }}
          >
            Start again
          </Button>
        )}
        <Button
          type="submit"
          loading={complete.isPending}
          disabled={!code.trim()}
        >
          Add plan
        </Button>
      </DialogFooter>
    </form>
  )
}

// Adding a plan with a code: the host gets a code from the vendor (GitHub for Copilot, OpenAI for
// ChatGPT, as Codex does), and the person enters it there and approves, while the dialog asks the host whether they
// have, until the plan is added, the code expires, or the dialog closes.
function CodeSignIn({
  kind,
  vendor,
  name,
  closed,
  onAdded,
}: {
  kind: Kind
  vendor: string
  name: string
  closed: React.RefObject<AbortController>
  onAdded: (account: Schemas["AgentAccountSummary"]) => void
}) {
  const start = useMutation({
    mutationFn: () =>
      answer(
        api.POST("/agent-accounts/sign-ins", {
          body: { kind, name },
        })
      ),
    onSuccess: (started) => approve.mutate(started),
  })
  const approve = useMutation({
    mutationFn: (started: Schemas["SignInStarted"]) =>
      waitForCode(
        started,
        () =>
          answer(
            api.POST("/agent-accounts/sign-ins/{id}/complete", {
              params: { path: { id: started.id } },
            })
          ),
        closed.current.signal
      ),
    onSuccess: (account) => account && onAdded(account),
  })

  if (start.isError || (approve.isError && !aborted(approve.error))) {
    return (
      <>
        <Alert variant="destructive">
          <AlertDescription>
            {messageOf(start.error ?? approve.error)}
          </AlertDescription>
        </Alert>
        <DialogFooter>
          <Button
            onClick={() => {
              approve.reset()
              start.mutate()
            }}
          >
            Start again
          </Button>
        </DialogFooter>
      </>
    )
  }
  if (!start.data?.userCode || approve.data === null) {
    return (
      <>
        {approve.data === null && (
          <p className="text-label text-muted-foreground">
            The code expired before it was approved.
          </p>
        )}
        <DialogFooter>
          <Button
            loading={start.isPending}
            onClick={() => {
              approve.reset()
              start.mutate()
            }}
          >
            {approve.data === null
              ? "Get a new code"
              : `Sign in with ${vendor}`}
          </Button>
        </DialogFooter>
      </>
    )
  }
  return (
    <CodeSteps
      code={start.data.userCode}
      url={start.data.url}
      vendor={vendor}
    />
  )
}
