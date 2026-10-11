import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { ArrowUpIcon, StopIcon } from "@phosphor-icons/react"
import { cn } from "cn"

import { Shortcut } from "@/components/shortcut"
import { Button } from "@/components/ui/button"
import { toast } from "@/components/ui/toast"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import { ApiError, answer, api, messageOf, type Schemas } from "../api"
import { ModelPicker, ThinkingPicker } from "../model-picker"
import { type Choice } from "../models"
import { chat as chatQuery, presets as presetsQuery } from "../queries"
import { chatStream } from "./chat-stream"
import { MessageBox } from "./message-box"

// Writing to the agent in a chat. While the agent works, a message goes into its running turn
// (steering), and Esc or the stop button ends the turn. An asleep nook starts waking as someone
// starts writing, so it's ready when they send. Whoever may use the chat's account picks its model,
// for the next turn on.

export function Composer({
  chat,
  harness,
  nookId,
  asleep,
  working,
  agent,
  proposing,
}: {
  chat: Schemas["ChatSummary"]
  harness: Schemas["HarnessSummary"] | undefined
  nookId: string
  asleep: boolean
  working: boolean
  agent: string
  /** Whose account the agent runs on, when the signed-in person's messages are only proposals. */
  proposing: string | null
}) {
  const chatId = chat.id
  const [draft, setDraft] = React.useState("")
  const empty = draft.trim() === ""
  const stopShown = working && empty
  const stream = chatStream(chatId)

  const send = useMutation({
    mutationFn: ({ text }: { text: string; key: string }) =>
      answer(
        api.POST("/chats/{id}/messages", {
          params: { path: { id: chatId } },
          body: { text },
        })
      ),
    onSuccess: (message, { key }) => stream.taken(key, message.id),
    onError: (error, { text, key }) => {
      stream.dropPending(key)
      setDraft((current) => (current.trim() === "" ? text : current))
      toast.add({
        type: "error",
        title: "Not sent",
        description:
          error instanceof ApiError
            ? error.message
            : "Can't reach the host. Your message is back in the box.",
      })
    },
  })
  const stop = useMutation({
    mutationFn: () =>
      answer(
        api.POST("/chats/{id}/stop", { params: { path: { id: chatId } } })
      ),
    onError: (error) =>
      toast.add({
        type: "error",
        title: "Not stopped",
        description: messageOf(error),
      }),
  })
  const wake = useMutation({
    mutationFn: () =>
      answer(
        api.POST("/nooks/{id}/wake", { params: { path: { id: nookId } } })
      ),
  })

  function submit() {
    const text = draft.trim()
    if (!text) {
      return
    }
    setDraft("")
    send.mutate({ text, key: stream.addPending(text) })
  }

  const placeholder = proposing
    ? `Propose a message; ${proposing} sends it on`
    : working
      ? `Steer ${agent} while it works`
      : `Message ${agent}`

  return (
    <div className="shrink-0 px-4 pb-4 md:px-5 md:pb-5">
      <MessageBox
        value={draft}
        onValueChange={setDraft}
        onSubmit={submit}
        onEscape={working ? () => stop.mutate() : undefined}
        onFocus={() => {
          if (asleep && !wake.isPending) {
            wake.mutate()
          }
        }}
        placeholder={placeholder}
        controls={
          harness && !proposing && <ChatModel chat={chat} harness={harness} />
        }
        action={
          // One button that follows intent: Stop while the agent works and nothing is typed, Send
          // (which steers a running turn) as soon as there is text. The two cross-fade with a little
          // blur, so they read as one thing changing.
          <div className="relative size-8">
            <Tooltip>
              <TooltipTrigger
                render={
                  <Button
                    variant="outline"
                    size="icon-sm"
                    aria-label="Stop the turn"
                    loading={stop.isPending}
                    onClick={() => stop.mutate()}
                    inert={!stopShown}
                    className={cn(
                      "absolute inset-0",
                      !stopShown && "scale-90 opacity-0 blur-[2px]"
                    )}
                  />
                }
              >
                <StopIcon weight="fill" className="size-3.5" />
              </TooltipTrigger>
              <TooltipContent className="gap-2">
                Stop the turn <Shortcut keys={["Esc"]} />
              </TooltipContent>
            </Tooltip>
            <Button
              type="submit"
              size="icon-sm"
              aria-label={working ? "Send to the running turn" : "Send"}
              disabled={empty}
              inert={stopShown}
              className={cn(
                "absolute inset-0",
                stopShown && "scale-90 opacity-0 blur-[2px]"
              )}
            >
              <ArrowUpIcon weight="bold" />
            </Button>
          </div>
        }
      />
    </div>
  )
}

// The chat's model and account, changed for its next turn. Its agent stays: its nook runs it. The
// accounts offered are the owner's whose the chat's is, the workspace's or the person's own.
function ChatModel({
  chat,
  harness,
}: {
  chat: Schemas["ChatSummary"]
  harness: Schemas["HarnessSummary"]
}) {
  const queryClient = useQueryClient()
  const presets = useQuery(presetsQuery(chat.workspaceId)).data ?? []
  const change = useMutation({
    mutationFn: (choice: Choice) =>
      answer(
        api.PUT("/chats/{id}/model", {
          params: { path: { id: chat.id } },
          body: {
            account: choice.account,
            model: choice.model,
            thinking: choice.thinking,
          },
        })
      ),
    onSuccess: (changed) =>
      queryClient.setQueryData(chatQuery(chat.id).queryKey, changed),
    onError: (error) =>
      toast.add({
        type: "error",
        title: "Not changed",
        description: messageOf(error),
      }),
  })
  const current: Choice = (change.isPending ? change.variables : null) ?? {
    harness: chat.harness,
    account: chat.account,
    model: chat.model,
    thinking: chat.thinking,
  }

  return (
    <>
      <ModelPicker
        workspaceId={chat.workspaceId}
        harness={harness}
        value={current}
        owner={chat.accountOwner ?? null}
        presets={presets}
        onChange={(choice) => change.mutate(choice)}
      />
      <ThinkingPicker
        levels={harness.thinkingLevels}
        value={current.thinking}
        onChange={(thinking) => change.mutate({ ...current, thinking })}
      />
    </>
  )
}
