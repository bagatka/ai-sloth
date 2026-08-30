import { useState } from "react"
import { Building2Icon, ChevronsUpDownIcon, PlusIcon } from "lucide-react"
import { WorkspaceOnboarding } from "@/components/workspace-onboarding"
import { Dialog, DialogContent, DialogTitle } from "@/components/ui/dialog"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import {
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from "@/components/ui/sidebar"
import { useSidebar } from "@/hooks/use-sidebar"
import type { Workspace, Workspaces } from "@/workspaces"

export function WorkspaceSwitcher({
  activeWorkspace,
  workspaces,
  onSelect,
}: {
  activeWorkspace: Workspace
  workspaces: Workspaces
  onSelect(workspace: Workspace): void
}) {
  const { isMobile } = useSidebar()
  const [showAddWorkspace, setShowAddWorkspace] = useState(false)

  function complete(workspace: Workspace) {
    onSelect(workspace)
    setShowAddWorkspace(false)
  }

  return (
    <>
      <SidebarMenu>
        <SidebarMenuItem>
          <DropdownMenu>
            <DropdownMenuTrigger
              render={
                <SidebarMenuButton
                  size="lg"
                  className="data-open:bg-sidebar-accent data-open:text-sidebar-accent-foreground"
                />
              }
            >
              <div className="flex aspect-square size-8 items-center justify-center bg-sidebar-primary text-sidebar-primary-foreground">
                <Building2Icon className="size-4" />
              </div>
              <div className="grid flex-1 text-left text-sm leading-tight">
                <span className="truncate font-medium">
                  {activeWorkspace.name}
                </span>
                <span className="truncate text-xs">Workspace</span>
              </div>
              <ChevronsUpDownIcon className="ml-auto" />
            </DropdownMenuTrigger>
            <DropdownMenuContent
              className="min-w-56"
              align="start"
              side={isMobile ? "bottom" : "right"}
              sideOffset={4}
            >
              <DropdownMenuGroup>
                <DropdownMenuLabel>Workspaces</DropdownMenuLabel>
                {workspaces.workspaces.map((workspace) => (
                  <DropdownMenuItem
                    key={workspace.id}
                    onClick={() => onSelect(workspace)}
                  >
                    <div className="flex size-6 items-center justify-center border">
                      <Building2Icon className="size-3.5" />
                    </div>
                    <span className="truncate">{workspace.name}</span>
                  </DropdownMenuItem>
                ))}
              </DropdownMenuGroup>
              <DropdownMenuSeparator />
              <DropdownMenuItem onClick={() => setShowAddWorkspace(true)}>
                <div className="flex size-6 items-center justify-center border">
                  <PlusIcon className="size-3.5" />
                </div>
                Add or join workspace
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </SidebarMenuItem>
      </SidebarMenu>

      <Dialog open={showAddWorkspace} onOpenChange={setShowAddWorkspace}>
        <DialogContent className="max-h-[calc(100svh-2rem)] overflow-y-auto sm:max-w-4xl">
          <DialogTitle className="sr-only">Add a workspace</DialogTitle>
          <WorkspaceOnboarding workspaces={workspaces} onComplete={complete} />
        </DialogContent>
      </Dialog>
    </>
  )
}
