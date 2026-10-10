import * as React from "react"
import { useMutation } from "@tanstack/react-query"
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
import { ApiError, answer, api } from "../api"
import { chatStream } from "./chat-stream"

// Writing to the agent: Enter sends, Shift+Enter adds a line. While the agent works, a message goes
// into its running turn (steering), and Esc or the stop button ends the turn. An asleep nook starts
// waking as someone starts writing, so it's ready when they send.

export function Composer({
  chatId,
  nookId,
  asleep,
  working,
  agent,
  proposing,
}: {
  chatId: string
  nookId: string
  asleep: boolean
  working: boolean
  agent: string
  /** Whose account the agent runs on, when the signed-in person's messages are only proposals. */
  proposing: string | null
}) {
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
        description:
          error instanceof ApiError ? error.message : "Can't reach the host.",
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
      <form
        className="mx-auto w-full max-w-[44rem] rounded-2xl border focus-field-within border-input bg-card shadow-field transition-[border-color,box-shadow]"
        onSubmit={(event) => {
          event.preventDefault()
          submit()
        }}
      >
        <textarea
          aria-label="Message"
          value={draft}
          onChange={(event) => setDraft(event.target.value)}
          onFocus={() => {
            if (asleep && !wake.isPending) {
              wake.mutate()
            }
          }}
          onKeyDown={(event) => {
            if (event.key === "Escape" && working) {
              event.preventDefault()
              stop.mutate()
              return
            }
            // Enter sends; Shift+Enter, or Enter while an input method composes, adds a line.
            if (
              event.key !== "Enter" ||
              event.shiftKey ||
              event.nativeEvent.isComposing
            ) {
              return
            }
            event.preventDefault()
            submit()
          }}
          placeholder={placeholder}
          rows={2}
          className="block [field-sizing:content] max-h-60 min-h-16 w-full resize-none bg-transparent px-[15px] pt-3.5 pb-1 text-base outline-none md:text-read"
        />
        <div className="flex items-center gap-1 px-2 pb-2">
          {/* One button that follows intent: Stop while the agent works and nothing is typed,
              Send (which steers a running turn) as soon as there is text. The two cross-fade with a
              little blur, so they read as one thing changing. */}
          <div className="relative ml-auto size-8">
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
        </div>
      </form>
    </div>
  )
}
