import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { ArrowSquareOutIcon, PlusIcon } from "@phosphor-icons/react"

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
import { accountKinds, agentAccounts, me } from "../queries"
import { NoRows, RemoveButton, Row, Rows, Section } from "./section"

// Agent accounts: what pays for the agents' work. A workspace's account is for everyone who works
// there; someone's own is for their chats alone. Most are added with a secret; a ChatGPT plan by
// signing in with ChatGPT.

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
    endpoint: "https://openrouter.ai/api",
  },
  OpenAIApiKey: {
    name: "OpenAI API key",
    description: "OpenAI's API, or any endpoint that speaks it",
    secret: "API key",
    endpoint: "https://openrouter.ai/api/v1",
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
    secret: "Token from claude setup-token",
    endpoint: null,
  },
  CopilotPlan: {
    name: "Copilot plan",
    description: "GitHub Copilot",
    secret: "GitHub token with the Copilot Requests permission",
    endpoint: null,
  },
}

// Where ChatGPT sends the browser back. ChatGPT returns only to this computer, where nothing
// listens, so the person brings the address back themselves.
const chatGptCallback = "http://127.0.0.1:1456/auth/callback"

// Own accounts show in every workspace, so any account change reads them all again.
const isAccountList = (query: { queryKey: readonly unknown[] }) =>
  query.queryKey[0] === "workspaces" && query.queryKey[2] === "agent-accounts"

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

function AddAccount({
  workspace,
  manager,
}: {
  workspace: Workspace
  manager: boolean
}) {
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
          <AccountForm
            workspace={workspace}
            manager={manager}
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
  onAdded,
}: {
  workspace: Workspace
  manager: boolean
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
          It's kept on the host, sealed, and never shown again; agents reach
          their model through the host, so no nook holds it.
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
      {chosen.addedBySignIn ? (
        <ChatGptSignIn name={name.trim() || names.name} onAdded={added} />
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

// Signing in with ChatGPT: the host makes the sign-in address; the person signs in there, and
// ChatGPT sends their browser to this computer's address, which doesn't load. They paste that
// address back, and the host finishes adding the plan.
function ChatGptSignIn({
  name,
  onAdded,
}: {
  name: string
  onAdded: (account: Schemas["AgentAccountSummary"]) => void
}) {
  const [returned, setReturned] = React.useState("")
  const start = useMutation({
    mutationFn: () =>
      answer(
        api.POST("/agent-accounts/sign-ins", {
          body: { kind: "ChatGptPlan", name, callback: chatGptCallback },
        })
      ),
  })
  const complete = useMutation({
    mutationFn: (id: string) =>
      answer(
        api.POST("/agent-accounts/sign-ins/{id}/complete", {
          params: { path: { id } },
          body: { returnedTo: returned.trim() },
        })
      ),
    onSuccess: onAdded,
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
            Sign in with ChatGPT
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
        if (returned.trim()) {
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
          Open ChatGPT to sign in
          <ArrowSquareOutIcon data-icon="inline-end" />
        </a>
        <Field data-invalid={complete.isError || undefined}>
          <FieldLabel htmlFor="returned-to">
            The address ChatGPT sent you to
          </FieldLabel>
          <Input
            id="returned-to"
            value={returned}
            placeholder="http://127.0.0.1:1456/auth/callback?code=…"
            onChange={(event) => setReturned(event.target.value)}
            aria-invalid={complete.isError || undefined}
          />
          {complete.isError ? (
            <FieldError>{messageOf(complete.error)}</FieldError>
          ) : (
            <FieldDescription>
              After you sign in, your browser opens a page that doesn't load.
              Copy its whole address from the address bar and paste it here,
              within ten minutes.
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
              setReturned("")
            }}
          >
            Start again
          </Button>
        )}
        <Button
          type="submit"
          loading={complete.isPending}
          disabled={!returned.trim()}
        >
          Add plan
        </Button>
      </DialogFooter>
    </form>
  )
}
