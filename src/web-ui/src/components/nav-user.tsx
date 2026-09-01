import { useState } from "react"
import {
  BadgeCheckIcon,
  ChevronsUpDownIcon,
  CreditCardIcon,
  LogOutIcon,
  Settings2Icon,
  SparklesIcon,
} from "lucide-react"
import { Avatar, AvatarFallback } from "@/components/ui/avatar"
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

export function NavUser({
  email,
  signingOut,
  onSignOut,
}: {
  email: string
  signingOut: boolean
  onSignOut(): Promise<string | null>
}) {
  const { isMobile } = useSidebar()
  const [error, setError] = useState<string | null>(null)
  const name = email.split("@", 1)[0] || email
  const initials = name.slice(0, 2).toUpperCase()

  async function signOut() {
    setError(null)
    setError(await onSignOut())
  }

  return (
    <SidebarMenu>
      <SidebarMenuItem>
        <DropdownMenu>
          <DropdownMenuTrigger
            render={
              <SidebarMenuButton size="lg" className="aria-expanded:bg-muted" />
            }
          >
            <UserIdentity email={email} name={name} initials={initials} />
            <ChevronsUpDownIcon className="ml-auto size-4" />
          </DropdownMenuTrigger>
          <DropdownMenuContent
            className="min-w-56"
            side={isMobile ? "bottom" : "right"}
            align="end"
            sideOffset={4}
          >
            <DropdownMenuGroup>
              <DropdownMenuLabel className="font-normal">
                <div className="flex items-center gap-2">
                  <UserIdentity email={email} name={name} initials={initials} />
                </div>
              </DropdownMenuLabel>
            </DropdownMenuGroup>
            <DropdownMenuSeparator />
            <DropdownMenuGroup>
              <DropdownMenuItem disabled>
                <SparklesIcon />
                Upgrade to Pro
              </DropdownMenuItem>
            </DropdownMenuGroup>
            <DropdownMenuSeparator />
            <DropdownMenuGroup>
              <DropdownMenuItem disabled>
                <BadgeCheckIcon />
                Account
              </DropdownMenuItem>
              <DropdownMenuItem disabled>
                <CreditCardIcon />
                Billing
              </DropdownMenuItem>
              <DropdownMenuItem disabled>
                <Settings2Icon />
                Settings
              </DropdownMenuItem>
            </DropdownMenuGroup>
            <DropdownMenuSeparator />
            <DropdownMenuItem
              onClick={signOut}
              disabled={signingOut}
              title={error ?? undefined}
            >
              <LogOutIcon />
              {signingOut
                ? "Signing out…"
                : error
                  ? "Retry log out"
                  : "Log out"}
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </SidebarMenuItem>
    </SidebarMenu>
  )
}

function UserIdentity({
  email,
  name,
  initials,
}: {
  email: string
  name: string
  initials: string
}) {
  return (
    <>
      <Avatar>
        <AvatarFallback>{initials}</AvatarFallback>
      </Avatar>
      <div className="grid flex-1 text-left text-sm leading-tight">
        <span className="truncate font-medium">{name}</span>
        <span className="truncate text-xs">{email}</span>
      </div>
    </>
  )
}
