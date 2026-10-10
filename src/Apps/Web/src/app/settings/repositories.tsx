import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  ArrowSquareOutIcon,
  GithubLogoIcon,
  LockSimpleIcon,
  PlusIcon,
} from "@phosphor-icons/react"

import { Alert, AlertDescription } from "@/components/ui/alert"
import { Button, buttonVariants } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Skeleton } from "@/components/ui/skeleton"
import { Spinner } from "@/components/ui/spinner"
import { toast } from "@/components/ui/toast"
import { answer, api, messageOf, seconds, type Schemas } from "../api"
import {
  availableRepositories,
  gitHubAccount,
  repositories as repositoriesQuery,
} from "../queries"
import {
  CopyableCode,
  NoRows,
  RemoveButton,
  Row,
  Rows,
  Section,
} from "./section"

// Repositories: the GitHub repositories a workspace's chats can start with. Each person connects
// their own GitHub account, through the host's GitHub App: managers add the repositories it
// reaches, and pushing goes as the person who pushes.

type Workspace = Schemas["WorkspaceSummary"]

export function RepositoriesSection({ workspace }: { workspace: Workspace }) {
  const queryClient = useQueryClient()
  const manager = workspace.access === "Manage"
  const account = useQuery(gitHubAccount)
  const list = useQuery(repositoriesQuery(workspace.id))
  const disconnect = useMutation({
    mutationFn: () => answer(api.DELETE("/github/account")),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["github"] }),
    onError: (error) =>
      toast.add({
        type: "error",
        title: "Not disconnected",
        description: messageOf(error),
      }),
  })
  const remove = useMutation({
    mutationFn: (id: string) =>
      answer(api.DELETE("/repositories/{id}", { params: { path: { id } } })),
    onSuccess: () =>
      queryClient.invalidateQueries({
        queryKey: repositoriesQuery(workspace.id).queryKey,
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
      id="repositories"
      title="Repositories"
      description={`The GitHub repositories chats in ${workspace.name} can start with. Pushing goes through your own GitHub account.`}
      action={
        manager &&
        account.data && (
          <AddRepository
            workspace={workspace}
            added={(list.data ?? []).map((repository) => repository.fullName)}
          />
        )
      }
    >
      <Rows label="Your GitHub account">
        {account.isPending ? (
          <NoRows>
            <Skeleton className="h-4 w-40" />
          </NoRows>
        ) : account.data ? (
          <Row
            title={
              <span className="flex items-center gap-2">
                <GithubLogoIcon className="size-4" />
                {account.data.login}
              </span>
            }
            detail="Your GitHub account, connected"
          >
            <Button
              variant="ghost"
              size="sm"
              loading={disconnect.isPending}
              onClick={() => disconnect.mutate()}
            >
              Disconnect
            </Button>
          </Row>
        ) : (
          <Row
            title={
              <span className="flex items-center gap-2">
                <GithubLogoIcon className="size-4" />
                GitHub
              </span>
            }
            detail="Connect your account to add repositories and push changes"
          >
            <ConnectGitHub />
          </Row>
        )}
      </Rows>
      <Rows label="Repositories">
        {list.isPending ? (
          <NoRows>
            <Skeleton className="h-4 w-48" />
          </NoRows>
        ) : (list.data ?? []).length === 0 ? (
          <NoRows>None yet. Chats without one start with empty files.</NoRows>
        ) : (
          (list.data ?? []).map((repository) => (
            <Row
              key={repository.id}
              title={repository.fullName}
              detail={`As ${repository.name} in its nooks, from ${repository.defaultBranch}`}
            >
              {manager && (
                <RemoveButton
                  what={repository.fullName}
                  consequence="New chats can't start with it. Nooks that have it keep their copy."
                  pending={
                    remove.isPending && remove.variables === repository.id
                  }
                  onRemove={() => remove.mutate(repository.id)}
                />
              )}
            </Row>
          ))
        )}
      </Rows>
    </Section>
  )
}

// Connecting GitHub: the host starts the connection, the person approves its code at GitHub, and
// the dialog asks the host whether they have, as often as GitHub allows, until they have, the code
// expires, or the dialog closes.
function ConnectGitHub() {
  const queryClient = useQueryClient()
  const [open, setOpen] = React.useState(false)
  const waiting = React.useRef<AbortController | null>(null)
  const start = useMutation({
    mutationFn: () => answer(api.POST("/github/connections")),
    onSuccess: (started) => {
      waiting.current = new AbortController()
      approve.mutate({ started, signal: waiting.current.signal })
    },
  })
  const approve = useMutation({
    mutationFn: waitForApproval,
    onSuccess: (account) => {
      if (!account) {
        return
      }
      void queryClient.invalidateQueries({ queryKey: ["github"] })
      toast.add({
        type: "success",
        title: `Connected GitHub as ${account.login}`,
      })
      setOpen(false)
    },
  })

  return (
    <Dialog
      open={open}
      onOpenChange={(opening) => {
        setOpen(opening)
        if (opening) {
          start.reset()
          approve.reset()
          start.mutate()
        } else {
          waiting.current?.abort()
        }
      }}
    >
      <DialogTrigger render={<Button size="sm" />}>
        Connect GitHub
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Connect GitHub</DialogTitle>
          <DialogDescription>
            Enter this code at GitHub to let AiSloth reach your repositories and
            push as you.
          </DialogDescription>
        </DialogHeader>
        {start.isError || (approve.isError && !aborted(approve.error)) ? (
          <Alert variant="destructive">
            <AlertDescription>
              {messageOf(start.error ?? approve.error)}
            </AlertDescription>
          </Alert>
        ) : !start.data ? (
          <Skeleton className="h-14 w-full rounded-xl" />
        ) : approve.data === null ? (
          <>
            <p className="text-label text-muted-foreground">
              The code expired before it was approved.
            </p>
            <Button
              onClick={() => {
                approve.reset()
                start.mutate()
              }}
            >
              Get a new code
            </Button>
          </>
        ) : (
          <>
            <CopyableCode code={start.data.userCode} />
            <a
              href={start.data.verificationUri}
              target="_blank"
              rel="noopener noreferrer"
              className={buttonVariants({ className: "w-full" })}
            >
              Open GitHub
              <ArrowSquareOutIcon data-icon="inline-end" />
            </a>
            <p
              role="status"
              className="flex items-center gap-2 text-label text-muted-foreground"
            >
              <Spinner className="size-3.5" />
              Waiting for you to approve it at GitHub
            </p>
          </>
        )}
      </DialogContent>
    </Dialog>
  )
}

type Started = Schemas["GitHubConnectionStarted"]

async function waitForApproval({
  started,
  signal,
}: {
  started: Started
  signal: AbortSignal
}): Promise<Schemas["GitHubAccount"] | null> {
  // GitHub says how often to ask; never more than once a second.
  let wait = Math.max(1, seconds(started.interval) ?? 5)
  while (Date.now() < Date.parse(started.expiresAt)) {
    await delay(wait * 1000, signal)
    const progress = await answer(
      api.POST("/github/connections/{id}/complete", {
        params: { path: { id: started.id } },
      })
    )
    if (progress.account) {
      return progress.account
    }
    wait = Math.max(1, seconds(progress.retryAfter) ?? wait)
  }
  // The code expired before anyone approved it.
  return null
}

function delay(milliseconds: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const timer = window.setTimeout(resolve, milliseconds)
    signal.addEventListener("abort", () => {
      window.clearTimeout(timer)
      reject(signal.reason)
    })
  })
}

function aborted(error: unknown): boolean {
  return error instanceof DOMException && error.name === "AbortError"
}

// Adding repositories the person's GitHub connection reaches. One that isn't listed needs the
// host's GitHub App installed on it first, at GitHub.
function AddRepository({
  workspace,
  added,
}: {
  workspace: Workspace
  added: readonly string[]
}) {
  const queryClient = useQueryClient()
  const [open, setOpen] = React.useState(false)
  const [filter, setFilter] = React.useState("")
  const available = useQuery({ ...availableRepositories, enabled: open })
  const add = useMutation({
    mutationFn: (fullName: string) =>
      answer(
        api.POST("/workspaces/{workspaceId}/repositories", {
          params: { path: { workspaceId: workspace.id } },
          body: { fullName },
        })
      ),
    onSuccess: (repository) => {
      void queryClient.invalidateQueries({
        queryKey: repositoriesQuery(workspace.id).queryKey,
      })
      toast.add({ type: "success", title: `Added ${repository.fullName}` })
    },
    onError: (error) =>
      toast.add({
        type: "error",
        title: "Not added",
        description: messageOf(error),
      }),
  })
  const shown = (available.data?.repositories ?? []).filter(
    (repository) =>
      !added.includes(repository.fullName) &&
      repository.fullName.toLowerCase().includes(filter.trim().toLowerCase())
  )

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger render={<Button variant="outline" size="sm" />}>
        <PlusIcon data-icon="inline-start" />
        Add
      </DialogTrigger>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Add repositories</DialogTitle>
          <DialogDescription>
            The repositories your GitHub account reaches through AiSloth's
            GitHub App.
          </DialogDescription>
        </DialogHeader>
        <Input
          value={filter}
          onChange={(event) => setFilter(event.target.value)}
          placeholder="Search"
          aria-label="Search repositories"
        />
        <ul
          aria-label="Repositories you can add"
          className="-mx-2 flex max-h-72 flex-col overflow-y-auto"
        >
          {available.isPending ? (
            <li className="px-2 py-2">
              <Skeleton className="h-4 w-48" />
            </li>
          ) : available.isError ? (
            <li className="px-2 py-2 text-label text-destructive">
              {messageOf(available.error)}
            </li>
          ) : shown.length === 0 ? (
            <li className="px-2 py-2 text-label text-muted-foreground">
              {filter.trim()
                ? `None of them has “${filter.trim()}” in its name.`
                : "Every repository it reaches is added."}
            </li>
          ) : (
            shown.map((repository) => (
              <li
                key={repository.fullName}
                className="flex items-center gap-3 rounded-lg px-2 py-1.5"
              >
                <span className="min-w-0 flex-1 truncate text-sm">
                  {repository.fullName}
                </span>
                {repository.private && (
                  <LockSimpleIcon
                    className="size-3.5 text-muted-foreground"
                    aria-label="Private"
                  />
                )}
                <Button
                  variant="outline"
                  size="xs"
                  loading={
                    add.isPending && add.variables === repository.fullName
                  }
                  onClick={() => add.mutate(repository.fullName)}
                >
                  Add
                </Button>
              </li>
            ))
          )}
        </ul>
        {available.data && (
          <p className="text-label text-muted-foreground">
            Not here?{" "}
            <a
              href={available.data.installUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="font-medium text-foreground underline underline-offset-4"
            >
              Install the app on more repositories
            </a>
            , then come back.
          </p>
        )}
      </DialogContent>
    </Dialog>
  )
}
