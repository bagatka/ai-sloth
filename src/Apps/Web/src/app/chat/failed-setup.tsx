import { useMutation } from "@tanstack/react-query"
import { useNavigate } from "@tanstack/react-router"
import {
  CaretDownIcon,
  WarningCircleIcon,
  WrenchIcon,
} from "@phosphor-icons/react"

import { Button } from "@/components/ui/button"
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible"
import {
  Popover,
  PopoverContent,
  PopoverDescription,
  PopoverHeader,
  PopoverTitle,
  PopoverTrigger,
} from "@/components/ui/popover"
import { toast } from "@/components/ui/toast"
import { answer, api, messageOf, type Schemas } from "../api"
import { chatStream } from "./chat-stream"
import type { Row } from "./transcript"

// A failed setup is the state of the chat's nook, so the chat's header shows it while the nook's
// latest setup run failed, with its output, and fixes it in a new chat: the same repositories,
// agent, and model in a fresh nook, where the fix runs the way new nooks will run it, and ships as
// its own pull request. The chat itself goes on in its nook as it is; its agent knows what failed.

// What a fix chat asks first; a fix chat offers no other.
const fixRequest =
  "Fix the setup that failed, run it again, and commit the fix."

type Setup = Extract<Row, { kind: "setup" }>
type Message = Extract<Row, { kind: "message" }>

export function FailedSetup({
  chat,
  nook,
  rows,
}: {
  chat: Schemas["ChatSummary"]
  nook: Schemas["NookSummary"] | undefined
  rows: readonly Row[]
}) {
  const navigate = useNavigate()
  const start = useMutation({
    mutationFn: async (from: Schemas["NookSummary"]) => {
      const fix = await answer(
        api.POST("/workspaces/{workspaceId}/chats", {
          params: { path: { workspaceId: chat.workspaceId } },
          body: {
            provider: from.provider,
            harness: chat.harness,
            account: chat.account,
            model: chat.model,
            thinking: chat.thinking,
            repositories: from.sources.map((source) => ({
              repository: source.repository,
              branch: source.branch,
            })),
          },
        })
      )
      const sent = await answer(
        api.POST("/chats/{id}/messages", {
          params: { path: { id: fix.id } },
          body: { text: fixRequest },
        })
      )
      return { fix, sent }
    },
    onSuccess: ({ fix, sent }) => {
      const stream = chatStream(fix.id)
      stream.taken(stream.addPending(fixRequest), sent.id)
      void navigate({ to: "/chats/$chatId", params: { chatId: fix.id } })
    },
    onError: (error) =>
      toast.add({
        type: "error",
        title: "Not started",
        description: messageOf(error),
      }),
  })

  const setup = rows.findLast((row): row is Setup => row.kind === "setup")
  if (!setup?.ended || setup.exitCode === 0) {
    return null
  }
  const fixing =
    rows.find((row): row is Message => row.kind === "message")?.text ===
    fixRequest

  return (
    <Popover>
      <PopoverTrigger
        render={<Button variant="ghost" size="sm" aria-label="Setup failed" />}
      >
        <WarningCircleIcon className="text-warning" />
        <span className="hidden sm:inline">Setup failed</span>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-96 max-w-[calc(100vw-2rem)]">
        <PopoverHeader>
          <PopoverTitle>
            The setup failed (exit code {setup.exitCode})
          </PopoverTitle>
          <PopoverDescription>
            {fixing
              ? "This chat fixes it."
              : "This nook may lack what it installs. A new chat fixes it in a fresh nook, for a pull request of its own."}
          </PopoverDescription>
        </PopoverHeader>
        <Collapsible className="flex flex-col gap-2.5">
          <div className="flex items-center gap-2">
            {!fixing && (
              <Button
                size="sm"
                loading={start.isPending}
                disabled={!nook}
                onClick={() => nook && start.mutate(nook)}
              >
                <WrenchIcon data-icon="inline-start" />
                Fix in a new chat
              </Button>
            )}
            {setup.output && (
              <CollapsibleTrigger
                render={<Button variant="ghost" size="sm" className="group" />}
              >
                Output
                <CaretDownIcon
                  data-icon="inline-end"
                  className="opacity-70 transition-transform group-data-panel-open:rotate-180"
                />
              </CollapsibleTrigger>
            )}
          </div>
          <CollapsibleContent>
            <pre className="max-h-64 overflow-auto rounded-lg bg-muted px-3 py-2 text-xs">
              {setup.output}
            </pre>
          </CollapsibleContent>
        </Collapsible>
      </PopoverContent>
    </Popover>
  )
}
