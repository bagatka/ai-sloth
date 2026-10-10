import { SidebarTrigger } from "@/components/ui/sidebar"
import { useCurrentWorkspace } from "../chat-list"
import { AccountsSection } from "./accounts"
import { InstructionsSection } from "./instructions"
import { PeopleSection } from "./people"
import { RepositoriesSection } from "./repositories"
import { SecretsSection } from "./secrets"

// Setting up a workspace: what pays for its agents, the repositories its chats start with, the
// secrets its nooks get, the instructions its agents follow, and who has access. Each section shows
// everyone what's there, and its changes to those who may make them.
export function SettingsPage() {
  const workspace = useCurrentWorkspace()
  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <title>
        {workspace ? `${workspace.name} settings – AiSloth` : "AiSloth"}
      </title>
      <header className="flex h-16 shrink-0 items-center gap-3 px-4 md:px-5">
        <SidebarTrigger className="-ml-1" />
        <div className="min-w-0 flex-1">
          <h1 className="truncate text-title">Settings</h1>
          <p className="truncate text-label text-muted-foreground">
            {workspace?.name}
          </p>
        </div>
      </header>
      <div className="min-h-0 flex-1 overflow-y-auto">
        {workspace && (
          // Choices and dialogs belong to one workspace.
          <div
            key={workspace.id}
            className="mx-auto flex max-w-[44rem] flex-col gap-12 px-4 pt-6 pb-16 md:px-5"
          >
            <AccountsSection workspace={workspace} />
            <RepositoriesSection workspace={workspace} />
            <SecretsSection workspace={workspace} />
            <InstructionsSection workspace={workspace} />
            <PeopleSection workspace={workspace} />
          </div>
        )}
      </div>
    </div>
  )
}
