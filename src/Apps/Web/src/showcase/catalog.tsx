import {
  ArrowLeftIcon,
  BellIcon,
  StackIcon,
  SquaresFourIcon,
  ChatCircleIcon,
  CursorClickIcon,
  SidebarSimpleIcon,
  TextboxIcon,
} from "@phosphor-icons/react"

import { ThemeSwitch } from "@/components/theme-switch"
import { Separator } from "@/components/ui/separator"
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarInset,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarProvider,
  SidebarTrigger,
} from "@/components/ui/sidebar"
import { Actions } from "@/showcase/actions"
import { Chat } from "@/showcase/chat"
import { Data } from "@/showcase/data"
import { Feedback } from "@/showcase/feedback"
import { Forms } from "@/showcase/forms"
import { Navigation } from "@/showcase/navigation"
import { Overlays } from "@/showcase/overlays"

const sections = [
  { id: "chat", title: "Chat", icon: ChatCircleIcon },
  { id: "actions", title: "Actions", icon: CursorClickIcon },
  { id: "forms", title: "Forms", icon: TextboxIcon },
  { id: "overlays", title: "Overlays", icon: StackIcon },
  { id: "navigation", title: "Navigation", icon: SidebarSimpleIcon },
  { id: "feedback", title: "Feedback", icon: BellIcon },
  { id: "data", title: "Data", icon: SquaresFourIcon },
]

// Every component on one page, with AiSloth's own content, to style them from. Opened at
// /components, and loaded only then.
export default function Catalog() {
  return (
    <SidebarProvider>
      <Sidebar>
        <SidebarHeader>
          <span className="px-2 py-1 text-sm font-semibold">AiSloth</span>
        </SidebarHeader>
        <SidebarContent>
          <SidebarGroup>
            <SidebarGroupLabel>Components</SidebarGroupLabel>
            <SidebarGroupContent>
              <SidebarMenu>
                {sections.map((section) => (
                  <SidebarMenuItem key={section.id}>
                    <SidebarMenuButton
                      render={<a href={`#${section.id}`} />}
                      tooltip={section.title}
                    >
                      <section.icon />
                      <span>{section.title}</span>
                    </SidebarMenuButton>
                  </SidebarMenuItem>
                ))}
              </SidebarMenu>
            </SidebarGroupContent>
          </SidebarGroup>
        </SidebarContent>
        <SidebarFooter>
          <SidebarMenu>
            <SidebarMenuItem>
              <SidebarMenuButton render={<a href="/" />}>
                <ArrowLeftIcon />
                <span>Chat screen</span>
              </SidebarMenuButton>
            </SidebarMenuItem>
          </SidebarMenu>
        </SidebarFooter>
      </Sidebar>
      <SidebarInset>
        <header className="sticky top-0 z-10 flex h-12 items-center gap-2 border-b bg-background px-4">
          <SidebarTrigger />
          <Separator orientation="vertical" className="h-4" />
          <span className="text-sm font-medium">Components</span>
          <ThemeSwitch className="ml-auto" />
        </header>
        <main className="mx-auto flex w-full max-w-6xl flex-col gap-16 p-6 pb-24">
          <Chat />
          <Actions />
          <Forms />
          <Overlays />
          <Navigation />
          <Feedback />
          <Data />
        </main>
      </SidebarInset>
    </SidebarProvider>
  )
}
