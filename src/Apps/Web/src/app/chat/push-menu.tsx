import { useMutation } from "@tanstack/react-query"
import {
  CaretDownIcon,
  GitBranchIcon,
  GitPullRequestIcon,
  UploadSimpleIcon,
} from "@phosphor-icons/react"

import { Button } from "@/components/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { toast } from "@/components/ui/toast"
import { ApiError, answer, api, type Schemas } from "../api"

// Shipping a chat's changes to GitHub with the person's connection: what isn't committed is
// committed as them, then each repository's commits go to one branch, their prefix and the chat's
// short ID, with a pull request when they ask. Each repository's outcome is its own.

type Pushed = Schemas["PushedSource"]

export function PushMenu({ chatId }: { chatId: string }) {
  const push = useMutation({
    mutationFn: (pullRequest: boolean) =>
      answer(
        api.POST("/chats/{id}/push", {
          params: { path: { id: chatId } },
          body: { pullRequest },
        })
      ),
    onSuccess: tell,
    onError: (error) =>
      toast.add({
        type: "error",
        title: "Not pushed",
        description:
          error instanceof ApiError
            ? error.code === "sources.github_not_connected"
              ? // Until the web app connects GitHub itself.
                "Connect your GitHub account first, with sloth: sloth github connect"
              : error.message
            : "Can't reach the host.",
      }),
  })

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={<Button size="sm" loading={push.isPending} />}
      >
        <UploadSimpleIcon data-icon="inline-start" />
        Push
        <CaretDownIcon data-icon="inline-end" className="opacity-70" />
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-auto min-w-52">
        <DropdownMenuItem onClick={() => push.mutate(true)}>
          <GitPullRequestIcon />
          Open a pull request
        </DropdownMenuItem>
        <DropdownMenuItem onClick={() => push.mutate(false)}>
          <GitBranchIcon />
          Push to a branch
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

// What happened to each repository, in one toast: a link to the pull request or branch when one
// repository went, and each repository's outcome when there are several.
function tell(pushed: readonly Pushed[]) {
  const went = pushed.filter((source) => source.branch && !source.problem)
  const refused = pushed.filter((source) => source.problem)
  if (went.length === 0 && refused.length === 0) {
    toast.add({
      title: "Nothing to push",
      description: "The agent hasn't changed its repositories yet.",
    })
    return
  }

  const link =
    went.length === 1 ? (went[0]!.pullRequestUrl ?? went[0]!.branchUrl) : null
  toast.add({
    type:
      refused.length === 0
        ? "success"
        : went.length === 0
          ? "error"
          : "warning",
    title: went.length > 0 ? `Pushed to ${went[0]!.branch}` : "Not pushed",
    description:
      pushed.length === 1 ? (
        (refused[0]?.problem ?? commits(went[0]!))
      ) : (
        <span className="whitespace-pre-line">
          {pushed
            .map((source) => `${source.source}: ${outcome(source)}`)
            .join("\n")}
        </span>
      ),
    actionProps: link
      ? {
          children: went[0]!.pullRequestUrl
            ? "Open pull request"
            : "Open branch",
          onClick: () => window.open(link, "_blank", "noopener"),
        }
      : undefined,
  })
}

function outcome(source: Pushed): string {
  if (source.problem) return `not pushed, ${source.problem}`
  if (!source.branch) return "no changes"
  return commits(source)
}

function commits(source: Pushed): string {
  return source.commits === 1 ? "1 commit" : `${source.commits} commits`
}
