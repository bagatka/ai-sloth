import {
  Breadcrumb,
  BreadcrumbEllipsis,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb"
import {
  NavigationMenu,
  NavigationMenuContent,
  NavigationMenuItem,
  NavigationMenuLink,
  NavigationMenuList,
  NavigationMenuTrigger,
} from "@/components/ui/navigation-menu"
import {
  Pagination,
  PaginationContent,
  PaginationEllipsis,
  PaginationItem,
  PaginationLink,
  PaginationNext,
  PaginationPrevious,
} from "@/components/ui/pagination"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { Section, Specimen } from "./section"

export function Navigation() {
  return (
    <Section id="navigation" title="Navigation">
      <Specimen name="Tabs">
        <Tabs defaultValue="chat" className="w-full">
          <TabsList>
            <TabsTrigger value="chat">Chat</TabsTrigger>
            <TabsTrigger value="changes">Changes</TabsTrigger>
            <TabsTrigger value="checkpoints">Checkpoints</TabsTrigger>
          </TabsList>
          <TabsContent value="chat" className="text-sm text-muted-foreground">
            The conversation with the agent.
          </TabsContent>
          <TabsContent
            value="changes"
            className="text-sm text-muted-foreground"
          >
            What differs from what the nook started with.
          </TabsContent>
          <TabsContent
            value="checkpoints"
            className="text-sm text-muted-foreground"
          >
            The nook’s files after each turn.
          </TabsContent>
        </Tabs>
      </Specimen>
      <Specimen name="Tabs, line variant">
        <Tabs defaultValue="members" className="w-full">
          <TabsList variant="line">
            <TabsTrigger value="members">Members</TabsTrigger>
            <TabsTrigger value="accounts">Agent accounts</TabsTrigger>
            <TabsTrigger value="secrets">Secrets</TabsTrigger>
          </TabsList>
        </Tabs>
      </Specimen>
      <Specimen name="Breadcrumb">
        <Breadcrumb>
          <BreadcrumbList>
            <BreadcrumbItem>
              <BreadcrumbLink href="#navigation">Acme</BreadcrumbLink>
            </BreadcrumbItem>
            <BreadcrumbSeparator />
            <BreadcrumbItem>
              <BreadcrumbEllipsis />
            </BreadcrumbItem>
            <BreadcrumbSeparator />
            <BreadcrumbItem>
              <BreadcrumbLink href="#navigation">Chats</BreadcrumbLink>
            </BreadcrumbItem>
            <BreadcrumbSeparator />
            <BreadcrumbItem>
              <BreadcrumbPage>Fix flaky checkout test</BreadcrumbPage>
            </BreadcrumbItem>
          </BreadcrumbList>
        </Breadcrumb>
      </Specimen>
      <Specimen name="Pagination">
        <Pagination>
          <PaginationContent>
            <PaginationItem>
              <PaginationPrevious href="#navigation" />
            </PaginationItem>
            <PaginationItem>
              <PaginationLink href="#navigation">1</PaginationLink>
            </PaginationItem>
            <PaginationItem>
              <PaginationLink href="#navigation" isActive>
                2
              </PaginationLink>
            </PaginationItem>
            <PaginationItem>
              <PaginationLink href="#navigation">3</PaginationLink>
            </PaginationItem>
            <PaginationItem>
              <PaginationEllipsis />
            </PaginationItem>
            <PaginationItem>
              <PaginationNext href="#navigation" />
            </PaginationItem>
          </PaginationContent>
        </Pagination>
      </Specimen>
      <Specimen name="Navigation menu">
        <NavigationMenu>
          <NavigationMenuList>
            <NavigationMenuItem>
              <NavigationMenuTrigger>Chats</NavigationMenuTrigger>
              <NavigationMenuContent>
                <ul className="grid w-72 gap-1 p-1">
                  {[
                    ["Running", "Agents working now"],
                    ["Waiting for you", "Turns that ended unread"],
                    ["Asleep", "Nooks that released their compute"],
                  ].map(([title, description]) => (
                    <li key={title}>
                      <NavigationMenuLink
                        href="#navigation"
                        className="flex-col items-start gap-0.5"
                      >
                        <span className="font-medium">{title}</span>
                        <span className="text-muted-foreground">
                          {description}
                        </span>
                      </NavigationMenuLink>
                    </li>
                  ))}
                </ul>
              </NavigationMenuContent>
            </NavigationMenuItem>
            <NavigationMenuItem>
              <NavigationMenuLink href="#navigation">
                Repositories
              </NavigationMenuLink>
            </NavigationMenuItem>
            <NavigationMenuItem>
              <NavigationMenuLink href="#navigation">
                Settings
              </NavigationMenuLink>
            </NavigationMenuItem>
          </NavigationMenuList>
        </NavigationMenu>
      </Specimen>
    </Section>
  )
}
