import { GitHubConnection } from "@/components/github-connection"
import { NavUser } from "@/components/nav-user"
import { WorkspaceSwitcher } from "@/components/workspace-switcher"
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarHeader,
  SidebarRail,
} from "@/components/ui/sidebar"
import type { GitHub } from "@/github"
import type { Workspace, Workspaces } from "@/workspaces"
import type { RepositoryCatalog } from "@/repository-navigation"
import { RepositoryNavigation } from "@/repository-navigation"

export function AppSidebar({
  activeWorkspace,
  catalog,
  email,
  github,
  workspaces,
  signingOut,
  onSelectWorkspace,
  onSelectSession,
  onSignOut,
  ...props
}: React.ComponentProps<typeof Sidebar> & {
  activeWorkspace: Workspace
  catalog: RepositoryCatalog
  email: string
  github: GitHub
  workspaces: Workspaces
  signingOut: boolean
  onSelectWorkspace(workspace: Workspace): void
  onSelectSession(sessionId: string): void
  onSignOut(): Promise<string | null>
}) {
  return (
    <Sidebar collapsible="icon" {...props}>
      <SidebarHeader>
        <WorkspaceSwitcher
          activeWorkspace={activeWorkspace}
          workspaces={workspaces}
          onSelect={onSelectWorkspace}
        />
      </SidebarHeader>
      <SidebarContent>
        <GitHubConnection github={github} />
        {github.connection ? (
          <RepositoryNavigation
            key={`${activeWorkspace.id}:${github.connection.githubUserId}`}
            catalog={catalog}
            workspaceId={activeWorkspace.id}
            onSelectSession={onSelectSession}
          />
        ) : null}
      </SidebarContent>
      <SidebarFooter>
        <NavUser email={email} signingOut={signingOut} onSignOut={onSignOut} />
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>
  )
}
