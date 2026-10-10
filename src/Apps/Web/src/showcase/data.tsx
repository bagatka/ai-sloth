import {
  CaretUpDownIcon,
  GitBranchIcon,
  ChatCircleIcon,
  DotsThreeIcon,
} from "@phosphor-icons/react"
import { Bar, BarChart, CartesianGrid, XAxis } from "recharts"

import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui/accordion"
import { AspectRatio } from "@/components/ui/aspect-ratio"
import {
  Avatar,
  AvatarBadge,
  AvatarFallback,
  AvatarGroup,
  AvatarGroupCount,
} from "@/components/ui/avatar"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import {
  Carousel,
  CarouselContent,
  CarouselItem,
  CarouselNext,
  CarouselPrevious,
} from "@/components/ui/carousel"
import {
  ChartContainer,
  ChartLegend,
  ChartLegendContent,
  ChartTooltip,
  ChartTooltipContent,
  type ChartConfig,
} from "@/components/ui/chart"
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible"
import {
  Item,
  ItemActions,
  ItemContent,
  ItemDescription,
  ItemGroup,
  ItemMedia,
  ItemSeparator,
  ItemTitle,
} from "@/components/ui/item"
import {
  ResizableHandle,
  ResizablePanel,
  ResizablePanelGroup,
} from "@/components/ui/resizable"
import { ScrollArea } from "@/components/ui/scroll-area"
import { Separator } from "@/components/ui/separator"
import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { Section, Specimen } from "./section"

const chats = [
  {
    title: "Fix flaky checkout test",
    harness: "Claude Code",
    state: "Running",
    when: "now",
  },
  {
    title: "Add dark mode to settings",
    harness: "Codex",
    state: "Asleep",
    when: "12 minutes ago",
  },
  {
    title: "Upgrade to Postgres 18",
    harness: "Copilot",
    state: "Ready",
    when: "1 hour ago",
  },
]

const agentMinutes = [
  { day: "Mon", claude: 182, codex: 96 },
  { day: "Tue", claude: 214, codex: 120 },
  { day: "Wed", claude: 160, codex: 141 },
  { day: "Thu", claude: 240, codex: 88 },
  { day: "Fri", claude: 198, codex: 132 },
]

const chartConfig = {
  claude: { label: "Claude Code", color: "var(--chart-1)" },
  codex: { label: "Codex", color: "var(--chart-2)" },
} satisfies ChartConfig

const log = Array.from(
  { length: 40 },
  (_, line) => `[setup] step ${line + 1}: installing dependencies for acme/web`
)

export function Data() {
  return (
    <Section id="data" title="Data">
      <Specimen name="Item: chat list" wide>
        <ItemGroup className="w-full">
          {chats.map((chat, index) => (
            <div key={chat.title}>
              {index > 0 && <ItemSeparator />}
              <Item>
                <ItemMedia variant="icon">
                  <ChatCircleIcon />
                </ItemMedia>
                <ItemContent>
                  <ItemTitle>{chat.title}</ItemTitle>
                  <ItemDescription>
                    {chat.harness}, {chat.when}
                  </ItemDescription>
                </ItemContent>
                <ItemActions>
                  <Badge variant="outline">{chat.state}</Badge>
                  <Button variant="ghost" size="icon-sm" aria-label="More">
                    <DotsThreeIcon />
                  </Button>
                </ItemActions>
              </Item>
            </div>
          ))}
        </ItemGroup>
      </Specimen>
      <Specimen name="Item variants" wide>
        <ItemGroup className="w-full gap-2">
          <Item variant="outline">
            <ItemMedia variant="icon">
              <GitBranchIcon />
            </ItemMedia>
            <ItemContent>
              <ItemTitle>acme/web</ItemTitle>
              <ItemDescription>
                Connected by Ada, 14 chats this week
              </ItemDescription>
            </ItemContent>
            <ItemActions>
              <Button variant="outline" size="sm">
                Start a chat
              </Button>
            </ItemActions>
          </Item>
          <Item variant="muted" size="sm">
            <ItemContent>
              <ItemTitle>acme/infra</ItemTitle>
              <ItemDescription>Not used yet</ItemDescription>
            </ItemContent>
          </Item>
        </ItemGroup>
      </Specimen>
      <Specimen name="Card">
        <Card className="w-full">
          <CardHeader>
            <CardTitle>Nook</CardTitle>
            <CardDescription>Ready on Azure, Claude Code image</CardDescription>
            <CardAction>
              <Badge>Ready</Badge>
            </CardAction>
          </CardHeader>
          <CardContent className="flex items-center gap-2 text-sm">
            <GitBranchIcon className="size-4" />
            <code>acme/web</code> on <code>ada/fix-flaky-checkout</code>
          </CardContent>
          <CardFooter className="gap-2">
            <Button size="sm">Open terminal</Button>
            <Button size="sm" variant="outline">
              Download files
            </Button>
          </CardFooter>
        </Card>
      </Specimen>
      <Specimen name="Avatar">
        <Avatar>
          <AvatarFallback>AL</AvatarFallback>
          <AvatarBadge />
        </Avatar>
        <Avatar size="lg">
          <AvatarFallback>GH</AvatarFallback>
        </Avatar>
        <AvatarGroup>
          <Avatar>
            <AvatarFallback>AL</AvatarFallback>
          </Avatar>
          <Avatar>
            <AvatarFallback>GH</AvatarFallback>
          </Avatar>
          <Avatar>
            <AvatarFallback>KJ</AvatarFallback>
          </Avatar>
          <AvatarGroupCount>+4</AvatarGroupCount>
        </AvatarGroup>
      </Specimen>
      <Specimen name="Table: nooks" wide>
        <Table>
          <TableCaption>Nooks in Acme</TableCaption>
          <TableHeader>
            <TableRow>
              <TableHead>Chat</TableHead>
              <TableHead>State</TableHead>
              <TableHead>Harness</TableHead>
              <TableHead className="text-right">Disk</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {chats.map((chat, index) => (
              <TableRow key={chat.title}>
                <TableCell className="font-medium">{chat.title}</TableCell>
                <TableCell>{chat.state}</TableCell>
                <TableCell>{chat.harness}</TableCell>
                <TableCell className="text-right">
                  {[7.2, 3.4, 5.9][index]} GB
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Specimen>
      <Specimen name="Chart: agent minutes this week" wide>
        <ChartContainer config={chartConfig} className="h-56 w-full">
          <BarChart accessibilityLayer data={agentMinutes}>
            <CartesianGrid vertical={false} />
            <XAxis dataKey="day" tickLine={false} axisLine={false} />
            <ChartTooltip content={<ChartTooltipContent />} />
            <ChartLegend content={<ChartLegendContent />} />
            <Bar dataKey="claude" fill="var(--color-claude)" radius={4} />
            <Bar dataKey="codex" fill="var(--color-codex)" radius={4} />
          </BarChart>
        </ChartContainer>
      </Specimen>
      <Specimen name="Accordion: instructions">
        <Accordion defaultValue={["workspace"]} className="w-full">
          <AccordionItem value="workspace">
            <AccordionTrigger>Acme’s instructions</AccordionTrigger>
            <AccordionContent>
              Run the tests before you push. Keep commits small.
            </AccordionContent>
          </AccordionItem>
          <AccordionItem value="yours">
            <AccordionTrigger>Your instructions</AccordionTrigger>
            <AccordionContent>
              Explain what you changed in one paragraph at the end.
            </AccordionContent>
          </AccordionItem>
        </Accordion>
      </Specimen>
      <Specimen name="Collapsible: a tool call">
        <Collapsible className="w-full">
          <div className="flex items-center justify-between gap-2">
            <span className="text-sm">
              Ran <code>npm test</code>
            </span>
            <CollapsibleTrigger
              render={
                <Button
                  variant="ghost"
                  size="icon-sm"
                  aria-label="Show output"
                />
              }
            >
              <CaretUpDownIcon />
            </CollapsibleTrigger>
          </div>
          <CollapsibleContent>
            <pre className="mt-2 rounded-lg bg-muted p-3 text-xs">
              {"PASS checkout.test.ts\nTests: 48 passed, 48 total"}
            </pre>
          </CollapsibleContent>
        </Collapsible>
      </Specimen>
      <Specimen name="Carousel: checkpoints">
        <Carousel className="mx-12 w-full max-w-xs">
          <CarouselContent>
            {[12, 11, 10, 9, 8].map((checkpoint) => (
              <CarouselItem key={checkpoint}>
                <Card>
                  <CardContent className="flex aspect-video items-center justify-center text-lg">
                    Checkpoint {checkpoint}
                  </CardContent>
                </Card>
              </CarouselItem>
            ))}
          </CarouselContent>
          <CarouselPrevious />
          <CarouselNext />
        </Carousel>
      </Specimen>
      <Specimen name="Aspect ratio: preview">
        <div className="w-full">
          <AspectRatio
            ratio={16 / 9}
            className="flex items-center justify-center rounded-lg bg-muted text-sm text-muted-foreground"
          >
            Preview of localhost:3000 in the nook
          </AspectRatio>
        </div>
      </Specimen>
      <Specimen name="Scroll area: setup output">
        <ScrollArea className="h-48 w-full rounded-md border">
          <pre className="p-3 text-xs">{log.join("\n")}</pre>
        </ScrollArea>
      </Specimen>
      <Specimen name="Resizable: chat and changes" wide>
        <ResizablePanelGroup
          orientation="horizontal"
          className="min-h-40 rounded-lg border"
        >
          <ResizablePanel defaultSize="60%">
            <div className="flex h-full items-center justify-center text-sm">
              Chat
            </div>
          </ResizablePanel>
          <ResizableHandle withHandle />
          <ResizablePanel defaultSize="40%">
            <div className="flex h-full items-center justify-center text-sm">
              Changes
            </div>
          </ResizablePanel>
        </ResizablePanelGroup>
      </Specimen>
      <Specimen name="Separator">
        <div className="flex w-full flex-col gap-3 text-sm">
          <span>Acme</span>
          <Separator />
          <div className="flex h-5 items-center gap-3">
            <span>Chats</span>
            <Separator orientation="vertical" />
            <span>Repositories</span>
            <Separator orientation="vertical" />
            <span>Settings</span>
          </div>
        </div>
      </Specimen>
    </Section>
  )
}
