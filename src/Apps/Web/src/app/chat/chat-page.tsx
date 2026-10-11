import { useQuery } from "@tanstack/react-query"
import { useParams } from "@tanstack/react-router"
import { ChatTeardropIcon } from "@phosphor-icons/react"

import { AgentWorking } from "@/components/agent-working"
import { NookState, type NookStateName } from "@/components/nook-state"
import { Shortcut } from "@/components/shortcut"
import { Button } from "@/components/ui/button"
import { MessageScrollerProvider } from "@/components/ui/message-scroller"
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@/components/ui/empty"
import { SidebarTrigger } from "@/components/ui/sidebar"
import { Skeleton } from "@/components/ui/skeleton"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import { ApiError } from "../api"
import { agentLabel } from "../models"
import {
  agentAccounts,
  chat as chatQuery,
  harnesses,
  me,
  nooks,
  people,
} from "../queries"
import { chatStream, useChat } from "./chat-stream"
import { ChangesPanel, DownloadFiles } from "./changes-panel"
import { Composer } from "./composer"
import { ContinueButton, ContinuesLine } from "./continue-chat"
import { PushMenu } from "./push-menu"
import { SetupBanner } from "./setup-banner"
import { TranscriptView } from "./transcript-view"
import { CacheGoneLine, PlanLimitLine, UsageMeter } from "./usage-meter"

// One chat as it happens: who wrote what, what the agent did, and a box to write to it.
export function ChatPage() {
  const { chatId } = useParams({ strict: false }) as { chatId: string }
  const chat = useQuery(chatQuery(chatId))
  const state = useChat(chatId)
  const person = useQuery(me).data
  const agents = useQuery(harnesses).data
  const nookList = useQuery({
    ...nooks(chat.data?.workspaceId ?? ""),
    enabled: !!chat.data,
  }).data
  // The chat's account, when it's the workspace's or the person's own.
  const account = useQuery({
    ...agentAccounts(chat.data?.workspaceId ?? ""),
    enabled: !!chat.data,
  }).data?.find((found) => found.id === chat.data?.account)
  const writers = state.transcript.rows.flatMap((row) =>
    row.kind === "message" ? [row.sentBy] : []
  )
  const names = useQuery(
    people([
      ...writers,
      ...(chat.data?.accountOwner ? [chat.data.accountOwner] : []),
    ])
  ).data

  if (chat.isError) {
    const missing = chat.error instanceof ApiError && chat.error.status === 404
    return (
      <Missing
        title={missing ? "This chat isn't here" : "Can't load this chat"}
        description={
          missing
            ? "It was deleted, or you don't have access to its nook."
            : "Can't reach the host. Check your connection, then try again."
        }
        retry={missing ? undefined : () => void chat.refetch()}
      />
    )
  }

  if (!chat.data || !person) {
    return <Loading />
  }

  const agent =
    agents?.find((harness) => harness.id === chat.data.harness)?.name ??
    chat.data.harness
  const nook = nookList?.items.find((item) => item.id === chat.data.nookId)
  // Until the stream has caught up, the chat's summary says whether its agent works.
  const working =
    state.transcript.sequence > 0 ? state.transcript.working : chat.data.working
  const nameOf = new Map(
    (names ?? []).map((someone) => [someone.id, someone.name])
  )
  const owner = chat.data.accountOwner
  const proposing =
    owner && owner !== person.id
      ? (nameOf.get(owner) ?? "the account's owner")
      : null
  const title = chat.data.title ?? "New chat"
  const nookState: NookStateName = working
    ? "working"
    : nook?.status === "Asleep"
      ? "asleep"
      : nook?.status === "Starting"
        ? "starting"
        : nook?.status === "Offline"
          ? "offline"
          : nook?.status === "Failed"
            ? "failed"
            : "ready"

  return (
    <>
      <title>{`${title} – AiSloth`}</title>
      <header className="flex h-16 shrink-0 items-center gap-3 px-4 md:px-5">
        <Tooltip>
          <TooltipTrigger render={<SidebarTrigger className="-ml-1" />} />
          <TooltipContent className="gap-2">
            Sidebar <Shortcut keys={["Mod", "B"]} />
          </TooltipContent>
        </Tooltip>
        <div className="min-w-0 flex-1">
          <h1 className="truncate text-title">{title}</h1>
          <p className="truncate text-label text-muted-foreground">
            {agentLabel(agent, chat.data.model, chat.data.thinking)}
          </p>
        </div>
        <div className="hidden sm:block">
          <NookState state={nookState} />
        </div>
        {/* Repositories' changes are reviewed and pushed; other files are downloaded. */}
        {nook && nook.sources.length > 0 ? (
          <>
            <ChangesPanel
              nookId={nook.id}
              checkpoint={state.transcript.checkpoint}
              counted={state.transcript.changes}
              working={working}
            />
            <PushMenu chatId={chatId} />
          </>
        ) : (
          nook && <DownloadFiles nookId={nook.id} />
        )}
      </header>
      {/* The transcript and the message box share one scroller: sending follows the newest output
          again. A chat gets its own, starting at its end. */}
      <MessageScrollerProvider key={chatId} autoScroll>
        {state.refused ? (
          <Missing
            title="Can't follow this chat"
            description="The host stopped sending its events. Try again in a moment."
            retry={() => chatStream(chatId).retry()}
          />
        ) : (
          <TranscriptView
            state={state}
            me={person.id}
            names={nameOf}
            agent={agent}
            start={
              chat.data.continues && (
                <ContinuesLine chatId={chat.data.continues} />
              )
            }
          />
        )}
        <SetupBanner
          chatId={chatId}
          transcript={state.transcript}
          proposing={proposing !== null}
        />
        <PlanLimitLine
          workspaceId={chat.data.workspaceId}
          account={account}
          working={working}
        />
        {proposing === null && (
          <CacheGoneLine
            chatId={chatId}
            transcript={state.transcript}
            action={
              <ContinueButton chat={chat.data} provider={nook?.provider} />
            }
          />
        )}
        {/* While the agent works, a line on the message box says so, wherever the transcript is read. */}
        {working && state.transcript.turnStartedAt && (
          <div className="shrink-0 px-4 pb-1.5 md:px-5">
            <div className="mx-auto w-full max-w-[44rem] px-[15px]">
              <AgentWorking
                since={Date.parse(state.transcript.turnStartedAt)}
              />
            </div>
          </div>
        )}
        <Composer
          key={chatId}
          chat={chat.data}
          harness={agents?.find((harness) => harness.id === chat.data.harness)}
          nookId={chat.data.nookId}
          asleep={nook?.status === "Asleep"}
          working={working}
          agent={agent}
          proposing={proposing}
          usage={
            <UsageMeter
              chatId={chatId}
              transcript={state.transcript}
              workspaceId={chat.data.workspaceId}
              account={account}
              actions={
                proposing === null && (
                  <ContinueButton chat={chat.data} provider={nook?.provider} />
                )
              }
            />
          }
        />
      </MessageScrollerProvider>
    </>
  )
}

function Loading() {
  return (
    <div className="flex min-h-0 flex-1 flex-col" aria-busy="true">
      <header className="flex h-16 shrink-0 items-center gap-3 px-4 md:px-5">
        <SidebarTrigger className="-ml-1" />
        <div className="flex flex-1 flex-col gap-1.5">
          <Skeleton className="h-4 w-48" />
          <Skeleton className="h-3 w-24" />
        </div>
      </header>
    </div>
  )
}

function Missing({
  title,
  description,
  retry,
}: {
  title: string
  description: string
  retry?: () => void
}) {
  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <header className="flex h-16 shrink-0 items-center gap-3 px-4 md:hidden">
        <SidebarTrigger className="-ml-1" />
      </header>
      <Empty>
        <EmptyHeader>
          <EmptyMedia variant="icon">
            <ChatTeardropIcon />
          </EmptyMedia>
          <EmptyTitle>{title}</EmptyTitle>
          <EmptyDescription>{description}</EmptyDescription>
        </EmptyHeader>
        {retry && (
          <Button variant="outline" onClick={retry}>
            Try again
          </Button>
        )}
      </Empty>
    </div>
  )
}
