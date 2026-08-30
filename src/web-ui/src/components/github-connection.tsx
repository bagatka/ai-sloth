import { GitBranchIcon, RefreshCwIcon, UnplugIcon } from "lucide-react"
import { Button } from "@/components/ui/button"
import { SidebarGroup, SidebarGroupLabel } from "@/components/ui/sidebar"
import type { GitHub } from "@/github"

export function GitHubConnection({ github }: { github: GitHub }) {
  if (github.status === "loading") {
    return (
      <SidebarGroup>
        <p className="px-2 text-xs text-muted-foreground">Loading GitHub…</p>
      </SidebarGroup>
    )
  }

  if (github.status === "error") {
    return (
      <SidebarGroup>
        <p className="px-2 text-xs text-destructive">{github.error}</p>
        <Button type="button" size="sm" variant="ghost" onClick={github.reload}>
          <RefreshCwIcon /> Retry
        </Button>
      </SidebarGroup>
    )
  }

  if (!github.connection) {
    return (
      <SidebarGroup>
        <SidebarGroupLabel>Source control</SidebarGroupLabel>
        <Button
          type="button"
          size="sm"
          variant="outline"
          onClick={github.connect}
          disabled={github.pending}
        >
          <GitBranchIcon />
          {github.pending ? "Connecting…" : "Connect GitHub"}
        </Button>
        {github.error ? (
          <p className="px-2 text-xs text-destructive">{github.error}</p>
        ) : null}
      </SidebarGroup>
    )
  }

  return (
    <SidebarGroup>
      <SidebarGroupLabel>GitHub</SidebarGroupLabel>
      <div className="flex items-center gap-2 px-2 text-xs">
        <GitBranchIcon className="size-4" />
        <span className="min-w-0 flex-1 truncate">
          @{github.connection.login}
        </span>
        <Button
          type="button"
          size="icon-sm"
          variant="ghost"
          title="Disconnect GitHub"
          onClick={github.disconnect}
          disabled={github.pending}
        >
          <UnplugIcon />
        </Button>
      </div>
      {github.installationUrl ? (
        <Button
          size="sm"
          variant="link"
          nativeButton={false}
          render={
            <a href={github.installationUrl} target="_blank" rel="noreferrer" />
          }
        >
          Select repositories
        </Button>
      ) : null}
      {github.error ? (
        <p className="px-2 text-xs text-destructive">{github.error}</p>
      ) : null}
    </SidebarGroup>
  )
}
