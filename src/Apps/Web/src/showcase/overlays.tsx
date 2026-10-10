import * as React from "react"
import {
  CopyIcon,
  GitPullRequestIcon,
  ChatCircleDotsIcon,
  MoonIcon,
  PencilSimpleIcon,
  StopIcon,
  TrashIcon,
  UploadSimpleIcon,
} from "@phosphor-icons/react"

import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog"
import { Shortcut } from "@/components/shortcut"
import { Button } from "@/components/ui/button"
import {
  Command,
  CommandDialog,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
  CommandSeparator,
  CommandShortcut,
} from "@/components/ui/command"
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuShortcut,
  ContextMenuTrigger,
} from "@/components/ui/context-menu"
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import {
  Drawer,
  DrawerClose,
  DrawerContent,
  DrawerDescription,
  DrawerFooter,
  DrawerHeader,
  DrawerTitle,
  DrawerTrigger,
} from "@/components/ui/drawer"
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuShortcut,
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Field, FieldGroup, FieldLabel } from "@/components/ui/field"
import {
  HoverCard,
  HoverCardContent,
  HoverCardTrigger,
} from "@/components/ui/hover-card"
import { Input } from "@/components/ui/input"
import {
  Menubar,
  MenubarContent,
  MenubarItem,
  MenubarMenu,
  MenubarSeparator,
  MenubarShortcut,
  MenubarTrigger,
} from "@/components/ui/menubar"
import {
  Popover,
  PopoverContent,
  PopoverDescription,
  PopoverHeader,
  PopoverTitle,
  PopoverTrigger,
} from "@/components/ui/popover"
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import { Section, Specimen } from "./section"

export function Overlays() {
  return (
    <Section id="overlays" title="Overlays">
      <Specimen name="Dialog">
        <Dialog>
          <DialogTrigger render={<Button variant="outline" />}>
            Invite people
          </DialogTrigger>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>Invite people to Acme</DialogTitle>
              <DialogDescription>
                Whoever uses the code first joins with the access you pick. It
                works for 7 days.
              </DialogDescription>
            </DialogHeader>
            <FieldGroup>
              <Field>
                <FieldLabel htmlFor="invite-note">Note for them</FieldLabel>
                <Input id="invite-note" placeholder="Welcome aboard" />
              </Field>
            </FieldGroup>
            <DialogFooter>
              <DialogClose render={<Button variant="outline" />}>
                Cancel
              </DialogClose>
              <Button>Create invite</Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </Specimen>
      <Specimen name="Alert dialog">
        <AlertDialog>
          <AlertDialogTrigger render={<Button variant="destructive" />}>
            Delete chat
          </AlertDialogTrigger>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>
                Delete “Fix flaky checkout test”?
              </AlertDialogTitle>
              <AlertDialogDescription>
                Its nook and checkpoints are deleted too. Changes you didn’t
                push are lost.
              </AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel>Keep chat</AlertDialogCancel>
              <AlertDialogAction variant="destructive">
                Delete chat
              </AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      </Specimen>
      <Specimen name="Sheet">
        <Sheet>
          <SheetTrigger render={<Button variant="outline" />}>
            Nook details
          </SheetTrigger>
          <SheetContent>
            <SheetHeader>
              <SheetTitle>Nook for “Fix flaky checkout test”</SheetTitle>
              <SheetDescription>
                Ready on Azure. Sleeps after 2 minutes unused.
              </SheetDescription>
            </SheetHeader>
            <dl className="grid grid-cols-2 gap-3 px-4 text-sm">
              <dt className="text-muted-foreground">Disk</dt>
              <dd>7.2 of 10 GB</dd>
              <dt className="text-muted-foreground">Memory</dt>
              <dd>1.1 of 4 GB</dd>
              <dt className="text-muted-foreground">Checkpoints</dt>
              <dd>12</dd>
            </dl>
          </SheetContent>
        </Sheet>
      </Specimen>
      <Specimen name="Drawer">
        <Drawer>
          <DrawerTrigger render={<Button variant="outline" />}>
            Changes
          </DrawerTrigger>
          <DrawerContent>
            <div className="mx-auto w-full max-w-md">
              <DrawerHeader>
                <DrawerTitle>3 files changed in acme/web</DrawerTitle>
                <DrawerDescription>
                  2 commits since the nook started, and changes not committed.
                </DrawerDescription>
              </DrawerHeader>
              <DrawerFooter>
                <Button>Push</Button>
                <DrawerClose render={<Button variant="outline" />}>
                  Close
                </DrawerClose>
              </DrawerFooter>
            </div>
          </DrawerContent>
        </Drawer>
      </Specimen>
      <Specimen name="Popover">
        <Popover>
          <PopoverTrigger render={<Button variant="outline" />}>
            Sleep settings
          </PopoverTrigger>
          <PopoverContent>
            <PopoverHeader>
              <PopoverTitle>Sleep</PopoverTitle>
              <PopoverDescription>
                A nook nobody used for 2 minutes releases its compute and wakes
                in about a second.
              </PopoverDescription>
            </PopoverHeader>
          </PopoverContent>
        </Popover>
      </Specimen>
      <Specimen name="Hover card and tooltip">
        <HoverCard>
          <HoverCardTrigger
            render={<a href="#overlays" className="text-sm underline" />}
          >
            acme/web
          </HoverCardTrigger>
          <HoverCardContent>
            <div className="flex flex-col gap-1 text-sm">
              <span className="font-medium">acme/web</span>
              <span className="text-muted-foreground">
                Connected by Ada. 14 chats started with it this week.
              </span>
            </div>
          </HoverCardContent>
        </HoverCard>
        <Tooltip>
          <TooltipTrigger
            render={<Button variant="outline" size="icon" aria-label="Stop" />}
          >
            <StopIcon />
          </TooltipTrigger>
          <TooltipContent>Stop the turn</TooltipContent>
        </Tooltip>
        <Tooltip>
          <TooltipTrigger
            render={<Button variant="outline" size="icon" aria-label="Push" />}
          >
            <UploadSimpleIcon />
          </TooltipTrigger>
          <TooltipContent>Push changes</TooltipContent>
        </Tooltip>
      </Specimen>
      <Specimen name="Dropdown menu">
        <ChatMenu />
      </Specimen>
      <Specimen name="Context menu">
        <ContextMenu>
          <ContextMenuTrigger className="flex h-24 w-full items-center justify-center rounded-lg border border-dashed text-sm text-muted-foreground">
            Right-click this chat
          </ContextMenuTrigger>
          <ContextMenuContent>
            <ContextMenuItem>
              <PencilSimpleIcon />
              Rename
            </ContextMenuItem>
            <ContextMenuItem>
              <CopyIcon />
              Copy link
              <ContextMenuShortcut>
                <Shortcut keys={["Mod", "C"]} plain />
              </ContextMenuShortcut>
            </ContextMenuItem>
            <ContextMenuSeparator />
            <ContextMenuItem variant="destructive">
              <TrashIcon />
              Delete
            </ContextMenuItem>
          </ContextMenuContent>
        </ContextMenu>
      </Specimen>
      <Specimen name="Menubar" wide>
        <Menubar>
          <MenubarMenu>
            <MenubarTrigger>Chat</MenubarTrigger>
            <MenubarContent>
              <MenubarItem>
                New chat{" "}
                <MenubarShortcut>
                  <Shortcut keys={["Mod", "N"]} plain />
                </MenubarShortcut>
              </MenubarItem>
              <MenubarItem>Rename</MenubarItem>
              <MenubarSeparator />
              <MenubarItem>Stop the turn</MenubarItem>
            </MenubarContent>
          </MenubarMenu>
          <MenubarMenu>
            <MenubarTrigger>Nook</MenubarTrigger>
            <MenubarContent>
              <MenubarItem>Open terminal</MenubarItem>
              <MenubarItem>Download files</MenubarItem>
              <MenubarSeparator />
              <MenubarItem>Put to sleep</MenubarItem>
            </MenubarContent>
          </MenubarMenu>
          <MenubarMenu>
            <MenubarTrigger>View</MenubarTrigger>
            <MenubarContent>
              <MenubarItem>Changes</MenubarItem>
              <MenubarItem>Checkpoints</MenubarItem>
            </MenubarContent>
          </MenubarMenu>
        </Menubar>
      </Specimen>
      <Specimen name="Command menu, inline and as a dialog" wide>
        <InlineCommands />
        <CommandPalette />
      </Specimen>
    </Section>
  )
}

function ChatMenu() {
  const [notify, setNotify] = React.useState(true)
  const [harness, setHarness] = React.useState("claude-code")

  return (
    <DropdownMenu>
      <DropdownMenuTrigger render={<Button variant="outline" />}>
        Chat options
      </DropdownMenuTrigger>
      <DropdownMenuContent className="w-56">
        <DropdownMenuGroup>
          <DropdownMenuLabel>Fix flaky checkout test</DropdownMenuLabel>
          <DropdownMenuItem>
            <PencilSimpleIcon />
            Rename
            <DropdownMenuShortcut>R</DropdownMenuShortcut>
          </DropdownMenuItem>
          <DropdownMenuItem>
            <GitPullRequestIcon />
            Open pull request
          </DropdownMenuItem>
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuCheckboxItem checked={notify} onCheckedChange={setNotify}>
          Tell me when a turn ends
        </DropdownMenuCheckboxItem>
        <DropdownMenuSub>
          <DropdownMenuSubTrigger>Harness for new chats</DropdownMenuSubTrigger>
          <DropdownMenuSubContent>
            <DropdownMenuRadioGroup value={harness} onValueChange={setHarness}>
              <DropdownMenuRadioItem value="claude-code">
                Claude Code
              </DropdownMenuRadioItem>
              <DropdownMenuRadioItem value="codex">Codex</DropdownMenuRadioItem>
              <DropdownMenuRadioItem value="copilot">
                Copilot
              </DropdownMenuRadioItem>
            </DropdownMenuRadioGroup>
          </DropdownMenuSubContent>
        </DropdownMenuSub>
        <DropdownMenuSeparator />
        <DropdownMenuItem variant="destructive">
          <TrashIcon />
          Delete
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

// cmdk scrolls its selected item into view when it mounts, which on a long page scrolls the
// window; starting with nothing selected keeps the page where it is.
function InlineCommands() {
  const [value, setValue] = React.useState("")

  return (
    <Command
      value={value}
      onValueChange={setValue}
      className="max-w-md rounded-lg border"
    >
      <Commands />
    </Command>
  )
}

function Commands() {
  return (
    <>
      <CommandInput placeholder="Search chats and commands" />
      <CommandList>
        <CommandEmpty>Nothing matches.</CommandEmpty>
        <CommandGroup heading="Chats">
          <CommandItem>Fix flaky checkout test</CommandItem>
          <CommandItem>Add dark mode to settings</CommandItem>
          <CommandItem>Upgrade to Postgres 18</CommandItem>
        </CommandGroup>
        <CommandSeparator />
        <CommandGroup heading="Commands">
          <CommandItem>
            <ChatCircleDotsIcon />
            Start a chat
            <CommandShortcut>
              <Shortcut keys={["Mod", "N"]} plain />
            </CommandShortcut>
          </CommandItem>
          <CommandItem>
            <MoonIcon />
            Switch theme
            <CommandShortcut>D</CommandShortcut>
          </CommandItem>
        </CommandGroup>
      </CommandList>
    </>
  )
}

function CommandPalette() {
  const [open, setOpen] = React.useState(false)

  return (
    <>
      <Button variant="outline" onClick={() => setOpen(true)}>
        Open command menu
      </Button>
      <CommandDialog open={open} onOpenChange={setOpen}>
        <Command>
          <Commands />
        </Command>
      </CommandDialog>
    </>
  )
}
