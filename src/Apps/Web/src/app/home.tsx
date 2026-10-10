import { ChatsCircleIcon } from "@phosphor-icons/react"

import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@/components/ui/empty"
import { SidebarTrigger } from "@/components/ui/sidebar"
import { ChatList, useChatItems, useCurrentWorkspace } from "./chat-list"

// No chat open. On a phone, where the sidebar is hidden, this is the list of chats; on a wider
// screen the sidebar already shows them.
export function ChatsHome() {
  const workspace = useCurrentWorkspace()
  const { items, loading } = useChatItems(workspace?.id)

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <title>{workspace ? `${workspace.name} – AiSloth` : "AiSloth"}</title>
      <header className="flex h-16 shrink-0 items-center gap-3 px-4 md:hidden">
        <SidebarTrigger className="-ml-1" />
        <h1 className="truncate text-title">{workspace?.name}</h1>
      </header>
      {!loading && items.length > 0 && (
        <ChatList items={items} className="overflow-y-auto pb-6 md:hidden" />
      )}
      {!loading && (
        <Empty className={items.length > 0 ? "hidden md:flex" : undefined}>
          <EmptyHeader>
            <EmptyMedia variant="icon">
              <ChatsCircleIcon />
            </EmptyMedia>
            {items.length > 0 ? (
              <>
                <EmptyTitle>Open a chat</EmptyTitle>
                <EmptyDescription>
                  Follow its agent as it works, and steer it with a message.
                </EmptyDescription>
              </>
            ) : (
              <>
                <EmptyTitle>No chats yet</EmptyTitle>
                <EmptyDescription>
                  Start one with sloth, such as{" "}
                  <code className="whitespace-nowrap">
                    sloth chat "Fix the flaky test"
                  </code>
                  , and it shows up here.
                </EmptyDescription>
              </>
            )}
          </EmptyHeader>
        </Empty>
      )}
    </div>
  )
}
