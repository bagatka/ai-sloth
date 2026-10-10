import * as React from "react"
import { cn } from "cn"
import {
  ArrowUpIcon,
  CaretRightIcon,
  CaretUpDownIcon,
  CheckIcon,
  GitBranchIcon,
  GitDiffIcon,
  MagnifyingGlassIcon,
  NotePencilIcon,
  PaperclipIcon,
  SquaresFourIcon,
  StopIcon,
  TerminalWindowIcon,
  UploadSimpleIcon,
} from "@phosphor-icons/react"

import {
  NookState,
  NookStateDot,
  type NookStateName,
} from "@/components/nook-state"
import { Shortcut } from "@/components/shortcut"
import { ThemeSwitch } from "@/components/theme-switch"
import { useShortcut } from "@/components/use-shortcut"
import { Avatar, AvatarFallback } from "@/components/ui/avatar"
import { Bubble, BubbleContent } from "@/components/ui/bubble"
import { Button, buttonVariants } from "@/components/ui/button"
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import {
  InputGroup,
  InputGroupAddon,
  InputGroupInput,
} from "@/components/ui/input-group"
import { Marker, MarkerContent } from "@/components/ui/marker"
import { toast } from "@/components/ui/toast"
import { Message, MessageContent } from "@/components/ui/message"
import {
  MessageScroller,
  MessageScrollerButton,
  MessageScrollerContent,
  MessageScrollerItem,
  MessageScrollerProvider,
  MessageScrollerViewport,
} from "@/components/ui/message-scroller"
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
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import { AgentWorking } from "./agent-working"

// The chat screen, composed from the components with made-up content, to judge the look as a
// whole: the sidebar on the ground, and the chat on one raised panel beside it, as on the landing.

type ChatSummary = {
  id: string
  title: string
  state: NookStateName
  when: string
}

const chatGroups: { name: string; chats: ChatSummary[] }[] = [
  {
    name: "Working",
    chats: [
      {
        id: "1",
        title: "Fix the flaky checkout test",
        state: "working",
        when: "now",
      },
    ],
  },
  {
    name: "Today",
    chats: [
      {
        id: "2",
        title: "Add dark mode to settings",
        state: "asleep",
        when: "12m",
      },
      { id: "3", title: "Upgrade to Postgres 18", state: "ready", when: "1h" },
      {
        id: "4",
        title: "Prepare billing-service",
        state: "asleep",
        when: "3h",
      },
    ],
  },
  {
    name: "Earlier",
    chats: [
      {
        id: "5",
        title: "Rename the orders API",
        state: "evicted",
        when: "Tue",
      },
      { id: "6", title: "Find why CI got slow", state: "failed", when: "Mon" },
    ],
  },
]

type Row =
  | { id: string; kind: "person"; text: string }
  | { id: string; kind: "agent"; first: boolean; text: React.ReactNode }
  | {
      id: string
      kind: "tool"
      command: string
      seconds: number
      output: string
    }
  | { id: string; kind: "marker"; text: string }
  | { id: string; kind: "working" }

const transcript: Row[] = [
  {
    id: "1",
    kind: "person",
    text: "The checkout test fails about one run in ten on CI. Find out why and fix it.",
  },
  {
    id: "2",
    kind: "agent",
    first: true,
    text: "The test waits for the payment iframe with a fixed 2 second timeout, and CI is slower when its cache is cold. I’ll wait for the iframe’s ready message instead.",
  },
  {
    id: "3",
    kind: "tool",
    command: "npx playwright test checkout --repeat-each 200",
    seconds: 41,
    output: "Running 200 tests using 4 workers\n  200 passed (41.2s)",
  },
  {
    id: "4",
    kind: "agent",
    first: false,
    text: (
      <>
        Changed <code>checkout.test.ts</code> and the payment fixture. The test
        passed 200 runs in a row.
      </>
    ),
  },
  { id: "5", kind: "marker", text: "Turn ended after 3 minutes" },
  { id: "6", kind: "person", text: "Nice. Push it and open a pull request." },
  {
    id: "7",
    kind: "tool",
    command: "git push -u origin ada/fix-flaky-checkout",
    seconds: 2,
    output:
      "To github.com:acme/web.git\n * [new branch]  ada/fix-flaky-checkout -> ada/fix-flaky-checkout",
  },
  {
    id: "8",
    kind: "agent",
    first: true,
    text: "Pushed, and opened pull request #482.",
  },
  { id: "9", kind: "marker", text: "Turn ended after 20 seconds" },
  {
    id: "10",
    kind: "person",
    text: "Fix the other two flaky tests you found too.",
  },
  {
    id: "11",
    kind: "agent",
    first: true,
    text: "Both wait with fixed timeouts as well. Starting with the orders tests.",
  },
  { id: "12", kind: "working" },
]

export default function ChatScreen() {
  // The turn runs until someone stops it; everything showing its state follows this one value.
  const [working, setWorking] = React.useState(true)
  const search = React.useRef<HTMLInputElement>(null)
  useShortcut({ key: "k", mod: true }, () => search.current?.focus())

  return (
    <SidebarProvider>
      <title>Fix the flaky checkout test – AiSloth</title>
      <ChatSidebar working={working} search={search} />
      <SidebarInset className="h-svh overflow-hidden md:h-[calc(100svh-1.25rem)]">
        <ChatHeader working={working} />
        <Transcript working={working} />
        <Composer working={working} onStop={() => setWorking(false)} />
      </SidebarInset>
    </SidebarProvider>
  )
}

function ChatSidebar({
  working,
  search,
}: {
  working: boolean
  search: React.Ref<HTMLInputElement>
}) {
  // A stopped chat leaves the Working group for the top of Today.
  const [current, ...others] = chatGroups[0].chats
  const groups = working
    ? chatGroups
    : [
        {
          ...chatGroups[1],
          chats: [
            { ...current, state: "ready" as const },
            ...chatGroups[1].chats,
          ],
        },
        ...chatGroups.slice(2),
        ...(others.length ? [{ ...chatGroups[0], chats: others }] : []),
      ]

  return (
    <Sidebar variant="inset">
      <SidebarHeader className="gap-2.5 pt-3.5">
        <WorkspaceSwitcher />
        <Button variant="outline" className="justify-start">
          <NotePencilIcon data-icon="inline-start" />
          New chat
        </Button>
        <InputGroup className="h-8">
          <InputGroupAddon>
            <MagnifyingGlassIcon />
          </InputGroupAddon>
          <InputGroupInput
            ref={search}
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
      <SidebarContent>
        {groups.map((group) => (
          <SidebarGroup key={group.name}>
            <SidebarGroupLabel>{group.name}</SidebarGroupLabel>
            <SidebarGroupContent>
              <SidebarMenu>
                {group.chats.map((chat) => (
                  <SidebarMenuItem key={chat.id}>
                    <SidebarMenuButton
                      isActive={chat.id === "1"}
                      render={<a href="/" />}
                    >
                      <NookStateDot state={chat.state} />
                      <span className="min-w-0 flex-1 truncate">
                        {chat.title}
                      </span>
                      <span className="text-xs text-muted-foreground tabular-nums">
                        {chat.when}
                      </span>
                    </SidebarMenuButton>
                  </SidebarMenuItem>
                ))}
              </SidebarMenu>
            </SidebarGroupContent>
          </SidebarGroup>
        ))}
      </SidebarContent>
      <SidebarFooter className="pb-5.5">
        <div className="flex items-center gap-2.5 px-2 py-1">
          <Avatar size="sm">
            <AvatarFallback>AL</AvatarFallback>
          </Avatar>
          <span className="min-w-0 flex-1 truncate text-sm font-medium">
            Ada Lovelace
          </span>
          <Tooltip>
            <TooltipTrigger
              render={
                <a
                  href="/components"
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
          <ThemeSwitch />
        </div>
      </SidebarFooter>
    </Sidebar>
  )
}

function WorkspaceSwitcher() {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={
          <button className="flex h-10 w-full press items-center gap-2.5 rounded-lg px-2 text-left focus-ring hover:bg-sidebar-accent active:bg-[color-mix(in_oklch,var(--sidebar-accent),var(--foreground)_6%)] aria-expanded:bg-sidebar-accent" />
        }
      >
        <span className="flex size-6 items-center justify-center rounded-md bg-card text-caption font-semibold shadow-raised">
          A
        </span>
        <span className="min-w-0 flex-1 truncate text-sm font-semibold">
          Acme
        </span>
        <CaretUpDownIcon className="size-4 text-muted-foreground" />
      </DropdownMenuTrigger>
      <DropdownMenuContent className="w-56">
        <DropdownMenuGroup>
          <DropdownMenuLabel>Workspaces</DropdownMenuLabel>
          <DropdownMenuItem>
            Acme
            <CheckIcon className="ml-auto" />
          </DropdownMenuItem>
          <DropdownMenuItem>Personal</DropdownMenuItem>
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuItem>Invite people</DropdownMenuItem>
        <DropdownMenuItem>Workspace settings</DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

function ChatHeader({ working }: { working: boolean }) {
  const [pushing, setPushing] = React.useState(false)

  // Made-up content: pushing takes a moment, then confirms.
  const push = () => {
    setPushing(true)
    window.setTimeout(() => {
      setPushing(false)
      toast.add({
        title: "Pushed to ada/fix-flaky-checkout",
        description: "Pull request #482 has all 4 changes.",
        type: "success",
      })
    }, 1200)
  }

  return (
    <header className="flex h-16 shrink-0 items-center gap-3 px-4 md:px-5">
      <Tooltip>
        <TooltipTrigger render={<SidebarTrigger className="-ml-1" />} />
        <TooltipContent className="gap-2">
          Sidebar <Shortcut keys={["Mod", "B"]} />
        </TooltipContent>
      </Tooltip>
      <div className="min-w-0 flex-1">
        <h1 className="truncate text-title">Fix the flaky checkout test</h1>
        <p className="flex min-w-0 items-center gap-1.5 text-label text-muted-foreground">
          <GitBranchIcon className="size-3.5 shrink-0" />
          <span className="truncate">
            <span className="font-mono text-caption">acme/web</span> on{" "}
            <span className="font-mono text-caption">
              ada/fix-flaky-checkout
            </span>
          </span>
        </p>
      </div>
      <div className="hidden sm:block">
        <NookState state={working ? "working" : "ready"} />
      </div>
      <Button variant="outline" size="sm">
        <GitDiffIcon data-icon="inline-start" />
        <span className="hidden sm:inline">Changes</span>
        <span className="text-muted-foreground tabular-nums">4</span>
      </Button>
      <Button size="sm" loading={pushing} onClick={push}>
        <UploadSimpleIcon data-icon="inline-start" />
        Push
      </Button>
    </header>
  )
}

function Transcript({ working }: { working: boolean }) {
  const rows: Row[] = working
    ? transcript
    : [
        ...transcript.filter((row) => row.kind !== "working"),
        { id: "13", kind: "marker", text: "You stopped the turn" },
      ]

  return (
    <div className="min-h-0 flex-1">
      <MessageScrollerProvider>
        <MessageScroller>
          <MessageScrollerViewport>
            <MessageScrollerContent className="mx-auto w-full max-w-[44rem] gap-3 px-4 pt-8 pb-10">
              {rows.map((row) => (
                <MessageScrollerItem
                  key={row.id}
                  messageId={row.id}
                  scrollAnchor={row.kind === "person"}
                >
                  <TranscriptRow row={row} />
                </MessageScrollerItem>
              ))}
            </MessageScrollerContent>
          </MessageScrollerViewport>
          <MessageScrollerButton />
        </MessageScroller>
      </MessageScrollerProvider>
    </div>
  )
}

function TranscriptRow({ row }: { row: Row }) {
  switch (row.kind) {
    case "person":
      return (
        <Message align="end" className="pt-5">
          <MessageContent>
            <Bubble variant="secondary">
              <BubbleContent>{row.text}</BubbleContent>
            </Bubble>
          </MessageContent>
        </Message>
      )
    case "agent":
      return (
        <div className="flex flex-col gap-1.5">
          {row.first && (
            <p className="flex items-baseline gap-2 text-label">
              <span className="font-semibold">Claude Code</span>
              <span className="text-muted-foreground">Sonnet</span>
            </p>
          )}
          <p className="text-read text-pretty">{row.text}</p>
        </div>
      )
    case "tool":
      return (
        <ToolCall
          command={row.command}
          seconds={row.seconds}
          output={row.output}
        />
      )
    case "marker":
      return (
        <Marker variant="separator" className="py-3 text-caption">
          <MarkerContent>{row.text}</MarkerContent>
        </Marker>
      )
    case "working":
      return <AgentWorking />
  }
}

function ToolCall({
  command,
  seconds,
  output,
}: {
  command: string
  seconds: number
  output: string
}) {
  return (
    <Collapsible>
      <CollapsibleTrigger
        render={
          <button className="group -mx-2 flex w-[calc(100%+1rem)] items-center gap-2 rounded-lg px-2 py-1.5 text-left text-label text-muted-foreground focus-ring transition-colors hover:bg-muted/70 hover:text-foreground active:bg-[color-mix(in_oklch,var(--muted),var(--foreground)_6%)] [&_code]:bg-transparent [&_code]:p-0 [&_code]:text-caption [&_code]:text-foreground/80" />
        }
      >
        <TerminalWindowIcon className="size-4 shrink-0" />
        <span className="shrink-0">Ran</span>
        <code className="min-w-0 truncate">{command}</code>
        <span className="ml-auto shrink-0 tabular-nums">{seconds}s</span>
        <CaretRightIcon className="size-3.5 shrink-0 transition-transform duration-200 ease-out group-data-panel-open:rotate-90" />
      </CollapsibleTrigger>
      <CollapsibleContent>
        <pre className="mt-1 overflow-x-auto rounded-lg bg-muted px-3.5 py-3 text-xs leading-relaxed">
          {output}
        </pre>
      </CollapsibleContent>
    </Collapsible>
  )
}

function Composer({
  working,
  onStop,
}: {
  working: boolean
  onStop: () => void
}) {
  const [model, setModel] = React.useState("sonnet")
  const [draft, setDraft] = React.useState("")
  const empty = draft.trim() === ""
  const stopShown = working && empty

  const send = () => {
    if (empty) return
    setDraft("")
  }

  return (
    <div className="shrink-0 px-4 pb-4 md:px-5 md:pb-5">
      <form
        className="mx-auto w-full max-w-[44rem] rounded-2xl border focus-field-within border-input bg-card shadow-field transition-[border-color,box-shadow]"
        onSubmit={(event) => {
          event.preventDefault()
          send()
        }}
      >
        <textarea
          aria-label="Message"
          value={draft}
          onChange={(event) => setDraft(event.target.value)}
          onKeyDown={(event) => {
            // Enter sends; Shift+Enter, or Enter while an input method composes, adds a line.
            if (event.key === "Escape" && working) {
              event.preventDefault()
              onStop()
              return
            }
            if (
              event.key !== "Enter" ||
              event.shiftKey ||
              event.nativeEvent.isComposing
            )
              return
            event.preventDefault()
            send()
          }}
          placeholder={
            working ? "Steer Claude Code while it works" : "Message Claude Code"
          }
          rows={2}
          className="block [field-sizing:content] max-h-60 min-h-16 w-full resize-none bg-transparent px-[15px] pt-3.5 pb-1 text-base outline-none md:text-read"
        />
        <div className="flex items-center gap-1 px-2 pb-2">
          <Tooltip>
            <TooltipTrigger
              render={
                <Button
                  variant="ghost"
                  size="icon-sm"
                  aria-label="Attach files"
                />
              }
            >
              <PaperclipIcon />
            </TooltipTrigger>
            <TooltipContent>Attach files</TooltipContent>
          </Tooltip>
          <DropdownMenu>
            <DropdownMenuTrigger
              render={
                <Button variant="ghost" size="sm" className="gap-1 px-2" />
              }
            >
              {model === "opus"
                ? "Opus"
                : model === "haiku"
                  ? "Haiku"
                  : "Sonnet"}
              <CaretUpDownIcon className="text-muted-foreground" />
            </DropdownMenuTrigger>
            <DropdownMenuContent className="w-48">
              <DropdownMenuGroup>
                <DropdownMenuLabel>Model</DropdownMenuLabel>
                <DropdownMenuRadioGroup value={model} onValueChange={setModel}>
                  <DropdownMenuRadioItem value="opus">
                    Opus
                  </DropdownMenuRadioItem>
                  <DropdownMenuRadioItem value="sonnet">
                    Sonnet
                  </DropdownMenuRadioItem>
                  <DropdownMenuRadioItem value="haiku">
                    Haiku
                  </DropdownMenuRadioItem>
                </DropdownMenuRadioGroup>
              </DropdownMenuGroup>
            </DropdownMenuContent>
          </DropdownMenu>
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
                    onClick={onStop}
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
