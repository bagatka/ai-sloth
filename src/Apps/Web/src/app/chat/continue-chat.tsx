import { useMutation, useQuery } from "@tanstack/react-query"
import { Link, useNavigate } from "@tanstack/react-router"
import { ArrowBendDownRightIcon } from "@phosphor-icons/react"

import { Button } from "@/components/ui/button"
import { toast } from "@/components/ui/toast"
import { answer, api, messageOf, type Schemas } from "../api"
import { chat as chatQuery } from "../queries"

// A new chat that continues this one, for when its context grew costly to read again: it starts in a
// copy of this chat's files as they are now, on the same agent, account, model, and thinking, with
// this conversation as a Markdown file its agent's instructions name. Its agent starts with an empty
// context and reads the file when it needs it. Nothing is summarized: the person writes the first
// message there.

export function ContinueButton({
  chat,
  provider,
  variant = "outline",
}: {
  chat: Schemas["ChatSummary"]
  /** Where the chat's nook runs; the new one runs there too. */
  provider: string | undefined
  variant?: "outline" | "ghost"
}) {
  const navigate = useNavigate()
  const start = useMutation({
    mutationFn: () =>
      answer(
        api.POST("/workspaces/{workspaceId}/chats", {
          params: { path: { workspaceId: chat.workspaceId } },
          body: {
            provider: provider!,
            harness: chat.harness,
            account: chat.account,
            model: chat.model!,
            thinking: chat.thinking,
            copyOf: chat.id,
            withConversation: true,
          },
        })
      ),
    onSuccess: (started) =>
      void navigate({ to: "/chats/$chatId", params: { chatId: started.id } }),
    onError: (error) =>
      toast.add({
        type: "error",
        title: "Not continued",
        description: messageOf(error),
      }),
  })
  return (
    <Button
      size="sm"
      variant={variant}
      disabled={!provider || !chat.model}
      loading={start.isPending}
      onClick={() => start.mutate()}
    >
      <ArrowBendDownRightIcon data-icon="inline-start" />
      Continue in a new chat
    </Button>
  )
}

/** At the top of a chat that continues another: which one, and what its agent has of it. */
export function ContinuesLine({ chatId }: { chatId: string }) {
  const earlier = useQuery(chatQuery(chatId)).data
  return (
    <p className="pb-2 text-label text-muted-foreground">
      Continues “
      <Link
        to="/chats/$chatId"
        params={{ chatId }}
        className="text-foreground underline-offset-4 hover:underline"
      >
        {earlier?.title ?? "an earlier chat"}
      </Link>
      ”. The agent starts with an empty context, and has that conversation as a
      file to read when it needs it.
    </p>
  )
}
