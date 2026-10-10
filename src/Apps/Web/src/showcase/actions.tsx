import {
  ArrowUpIcon,
  CaretDownIcon,
  GitPullRequestIcon,
  ChatCircleIcon,
  PlusIcon,
  StopIcon,
  TerminalWindowIcon,
  GitDiffIcon,
  TextBIcon,
} from "@phosphor-icons/react"

import { Shortcut } from "@/components/shortcut"
import { Button } from "@/components/ui/button"
import { ButtonGroup, ButtonGroupText } from "@/components/ui/button-group"
import { Input } from "@/components/ui/input"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Toggle } from "@/components/ui/toggle"
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group"
import { Section, Specimen } from "./section"

export function Actions() {
  return (
    <Section id="actions" title="Actions">
      <Specimen name="Button variants" wide>
        <Button>Start chat</Button>
        <Button variant="secondary">Send on</Button>
        <Button variant="outline">Push</Button>
        <Button variant="ghost">Cancel</Button>
        <Button variant="destructive">Delete chat</Button>
        <Button variant="link">Open pull request</Button>
      </Specimen>
      <Specimen name="Button sizes and icons">
        <Button size="xs">Steer</Button>
        <Button size="sm">Steer</Button>
        <Button>Steer</Button>
        <Button size="lg">Steer</Button>
        <Button size="icon" variant="outline" aria-label="Stop">
          <StopIcon />
        </Button>
        <Button size="icon">
          <ArrowUpIcon />
          <span className="sr-only">Send</span>
        </Button>
        <Button variant="outline">
          <PlusIcon data-icon="inline-start" />
          New chat
        </Button>
        <Button disabled>Nothing to push</Button>
        <Button loading>Pushing</Button>
        <Button variant="outline" loading>
          Starting nook
        </Button>
      </Specimen>
      <Specimen name="Button group">
        <ButtonGroup>
          <Button variant="outline">
            <GitPullRequestIcon data-icon="inline-start" />
            Push
          </Button>
          <DropdownMenu>
            <DropdownMenuTrigger
              render={<Button variant="outline" size="icon" />}
            >
              <CaretDownIcon />
              <span className="sr-only">More ways to push</span>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem>Push to a new branch</DropdownMenuItem>
              <DropdownMenuItem>Push and open a pull request</DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </ButtonGroup>
      </Specimen>
      <Specimen name="Toggle and toggle group">
        <Toggle aria-label="Bold">
          <TextBIcon />
        </Toggle>
        <ToggleGroup defaultValue={["chat"]} variant="outline">
          <ToggleGroupItem value="chat">
            <ChatCircleIcon />
            Chat
          </ToggleGroupItem>
          <ToggleGroupItem value="changes">
            <GitDiffIcon />
            Changes
          </ToggleGroupItem>
          <ToggleGroupItem value="terminal">
            <TerminalWindowIcon />
            Terminal
          </ToggleGroupItem>
        </ToggleGroup>
      </Specimen>
      <Specimen name="Joined toggle group and button group with an input">
        <ToggleGroup defaultValue={["sonnet"]} variant="outline" spacing={0}>
          <ToggleGroupItem value="opus">Opus</ToggleGroupItem>
          <ToggleGroupItem value="sonnet">Sonnet</ToggleGroupItem>
          <ToggleGroupItem value="haiku">Haiku</ToggleGroupItem>
        </ToggleGroup>
        <ButtonGroup>
          <ButtonGroupText>Branch prefix</ButtonGroupText>
          <Input
            defaultValue="ada/"
            aria-label="Branch prefix"
            className="w-28"
          />
          <Button variant="outline">Save</Button>
        </ButtonGroup>
      </Specimen>
      <Specimen name="Keyboard keys">
        <span className="flex items-center gap-2 text-sm">
          Search chats
          <Shortcut keys={["Mod", "K"]} />
        </span>
        <span className="flex items-center gap-2 text-sm">
          Send <Shortcut keys={["Enter"]} />
        </span>
        <span className="flex items-center gap-2 text-sm">
          Stop <Shortcut keys={["Esc"]} />
        </span>
      </Specimen>
    </Section>
  )
}
