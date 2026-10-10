import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Outlet, useNavigate } from "@tanstack/react-router"
import {
  CaretUpDownIcon,
  CheckIcon,
  MagnifyingGlassIcon,
  SignOutIcon,
  SquaresFourIcon,
} from "@phosphor-icons/react"

import { Shortcut } from "@/components/shortcut"
import { ThemeSwitch } from "@/components/theme-switch"
import { useShortcut } from "@/components/use-shortcut"
import { Avatar, AvatarFallback } from "@/components/ui/avatar"
import { Button, buttonVariants } from "@/components/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import {
  InputGroup,
  InputGroupAddon,
  InputGroupInput,
} from "@/components/ui/input-group"
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarHeader,
  SidebarInset,
  SidebarProvider,
} from "@/components/ui/sidebar"
import { Skeleton } from "@/components/ui/skeleton"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import { answer, api } from "./api"
import { ChatList, useChatItems, useCurrentWorkspace } from "./chat-list"
import { me, workspaces } from "./queries"
import { chooseWorkspace } from "./workspace"

// The signed-in app: the workspace's chats in the sidebar, and the open one beside them.
export function AppShell() {
  const search = React.useRef<HTMLInputElement>(null)
  useShortcut({ key: "k", mod: true }, () => search.current?.focus())

  return (
    <SidebarProvider>
      <AppSidebar search={search} />
      <SidebarInset className="h-svh overflow-hidden md:h-[calc(100svh-1.25rem)]">
        <Outlet />
      </SidebarInset>
    </SidebarProvider>
  )
}

function AppSidebar({ search }: { search: React.Ref<HTMLInputElement> }) {
  const workspace = useCurrentWorkspace()
  const { items, loading } = useChatItems(workspace?.id)
  const [filter, setFilter] = React.useState("")

  return (
    <Sidebar variant="inset">
      <SidebarHeader className="gap-2.5 pt-3.5">
        <WorkspaceSwitcher current={workspace} />
        <InputGroup className="h-8">
          <InputGroupAddon>
            <MagnifyingGlassIcon />
          </InputGroupAddon>
          <InputGroupInput
            ref={search}
            value={filter}
            onChange={(event) => setFilter(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "Escape") {
                setFilter("")
              }
            }}
            placeholder="Search"
            aria-label="Search chats"
          />
          <InputGroupAddon align="inline-end">
            <span className="text-xs text-muted-foreground">
              <Shortcut keys={["Mod", "K"]} plain />
            </span>
          </InputGroupAddon>
        </InputGroup>
      </SidebarHeader>
      <SidebarContent className="pt-1">
        {loading ? (
          <div className="flex flex-col gap-3 px-4 py-2" aria-busy="true">
            {[60, 80, 45, 70].map((width) => (
              <Skeleton
                key={width}
                className="h-4"
                style={{ width: `${width}%` }}
              />
            ))}
          </div>
        ) : (
          <ChatList items={items} filter={filter} />
        )}
      </SidebarContent>
      <SidebarFooter className="pb-5.5">
        <Person />
      </SidebarFooter>
    </Sidebar>
  )
}

function WorkspaceSwitcher({
  current,
}: {
  current: ReturnType<typeof useCurrentWorkspace>
}) {
  const all = useQuery(workspaces).data?.items ?? []
  const navigate = useNavigate()

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        disabled={!current}
        render={
          <button className="flex h-10 w-full press items-center gap-2.5 rounded-lg px-2 text-left focus-ring hover:bg-sidebar-accent active:bg-[color-mix(in_oklch,var(--sidebar-accent),var(--foreground)_6%)] aria-expanded:bg-sidebar-accent" />
        }
      >
        <span className="flex size-6 items-center justify-center rounded-md bg-card text-caption font-semibold shadow-raised">
          {initials(current?.name ?? "")}
        </span>
        <span className="min-w-0 flex-1 truncate text-sm font-semibold">
          {current?.name ?? "No workspace"}
        </span>
        <CaretUpDownIcon className="size-4 text-muted-foreground" />
      </DropdownMenuTrigger>
      <DropdownMenuContent className="w-56">
        <DropdownMenuGroup>
          <DropdownMenuLabel>Workspaces</DropdownMenuLabel>
          {all.map((workspace) => (
            <DropdownMenuItem
              key={workspace.id}
              onClick={() => {
                chooseWorkspace(workspace.id)
                if (workspace.id !== current?.id) {
                  void navigate({ to: "/" })
                }
              }}
            >
              <span className="min-w-0 flex-1 truncate">{workspace.name}</span>
              {workspace.id === current?.id && <CheckIcon />}
            </DropdownMenuItem>
          ))}
        </DropdownMenuGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

function Person() {
  const person = useQuery(me).data
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const signOut = useMutation({
    mutationFn: () => answer(api.POST("/sign-out")),
    onSettled: () => {
      queryClient.clear()
      void navigate({ to: "/sign-in" })
    },
  })

  return (
    <div className="flex items-center gap-1.5 px-2 py-1">
      <Avatar size="sm">
        <AvatarFallback>{initials(person?.name ?? "")}</AvatarFallback>
      </Avatar>
      <span className="ml-1 min-w-0 flex-1 truncate text-sm font-medium">
        {person?.name}
      </span>
      {import.meta.env.DEV && (
        <Tooltip>
          <TooltipTrigger
            render={
              <a
                href="design/components"
                aria-label="Components"
                className={buttonVariants({
                  variant: "ghost",
                  size: "icon-sm",
                })}
              />
            }
          >
            <SquaresFourIcon />
          </TooltipTrigger>
          <TooltipContent>Components</TooltipContent>
        </Tooltip>
      )}
      <ThemeSwitch />
      <Tooltip>
        <TooltipTrigger
          render={
            <Button
              variant="ghost"
              size="icon-sm"
              aria-label="Sign out"
              loading={signOut.isPending}
              onClick={() => signOut.mutate()}
            />
          }
        >
          <SignOutIcon />
        </TooltipTrigger>
        <TooltipContent>Sign out</TooltipContent>
      </Tooltip>
    </div>
  )
}

function initials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean)
  return words
    .slice(0, 2)
    .map((word) => word[0]!.toUpperCase())
    .join("")
}
