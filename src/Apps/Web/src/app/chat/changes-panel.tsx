import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { DownloadSimpleIcon, GitDiffIcon } from "@phosphor-icons/react"

import {
  ChangeCounts,
  DiffList,
  FileTree,
  inTreeOrder,
  preloadCode,
  type DiffLayout,
  type DiffListHandle,
  type DiffListView,
} from "@/components/code"
import { Alert, AlertDescription } from "@/components/ui/alert"
import { Button, buttonVariants } from "@/components/ui/button"
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyTitle,
} from "@/components/ui/empty"
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select"
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet"
import { Skeleton } from "@/components/ui/skeleton"
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import { ApiError } from "../api"
import { apiUrl } from "../host"
import { changes as changesQuery } from "../queries"
import { changedFiles, type ChangedFile } from "./changes"

// A chat's changes on demand: every changed file's diff in one scroll, beside a tree of the files
// that selects the one being read, marks the ones on screen, and jumps to the one picked. The host
// reads them each time the panel opens, which wakes an asleep nook.

export function ChangesPanel({ nookId }: { nookId: string }) {
  const [open, setOpen] = React.useState(false)
  const read = useQuery({ ...changesQuery(nookId), enabled: open })
  const sources = read.data ?? []
  // With several repositories, each file's path starts with its repository's name.
  const several = sources.length > 1
  const files = inTreeOrder(
    sources.flatMap((source) =>
      changedFiles(source.patch).map((file) =>
        several ? { ...file, path: `${source.source}/${file.path}` } : file
      )
    )
  )
  const leftOut = sources.filter((source) => source.truncated)
  const totals = files.reduce(
    (sum, file) => ({
      additions: sum.additions + file.additions,
      deletions: sum.deletions + file.deletions,
    }),
    { additions: 0, deletions: 0 }
  )
  const [layout, setLayout] = React.useState<DiffLayout>("unified")

  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetTrigger
        onPointerEnter={preloadCode}
        render={<Button variant="outline" size="sm" />}
      >
        <GitDiffIcon data-icon="inline-start" />
        <span className="hidden sm:inline">Changes</span>
      </SheetTrigger>
      <SheetContent className="gap-0 p-0 data-[side=right]:w-[calc(100%-1.5rem)] data-[side=right]:sm:max-w-5xl">
        <div className="flex h-16 shrink-0 items-center gap-3 pr-16 pl-5">
          <div className="min-w-0 flex-1">
            <SheetTitle className="text-title">Changes</SheetTitle>
            <SheetDescription className="flex items-center gap-2 text-label">
              {read.isPending ? (
                "Reading what changed"
              ) : read.isError ? (
                "Couldn't read them"
              ) : (
                <>
                  {files.length === 1 ? "1 file" : `${files.length} files`} in{" "}
                  {names(sources.map((source) => source.source))}
                  <ChangeCounts
                    additions={totals.additions}
                    deletions={totals.deletions}
                  />
                </>
              )}
            </SheetDescription>
          </div>
          <ToggleGroup
            value={[layout]}
            onValueChange={(value) =>
              value[0] && setLayout(value[0] as DiffLayout)
            }
            variant="outline"
            spacing={0}
            size="sm"
            className="hidden md:flex"
          >
            <ToggleGroupItem value="unified">Unified</ToggleGroupItem>
            <ToggleGroupItem value="split">Split</ToggleGroupItem>
          </ToggleGroup>
          <DownloadFiles nookId={nookId} />
        </div>
        {read.isPending ? (
          <div className="flex flex-col gap-3 px-5 py-2" aria-busy="true">
            {[70, 45, 85, 60].map((width) => (
              <Skeleton
                key={width}
                className="h-4"
                style={{ width: `${width}%` }}
              />
            ))}
          </div>
        ) : read.isError ? (
          <Empty>
            <EmptyHeader>
              <EmptyTitle>Couldn't read the changes</EmptyTitle>
              <EmptyDescription>
                {read.error instanceof ApiError
                  ? read.error.message
                  : "Can't reach the host."}
              </EmptyDescription>
            </EmptyHeader>
            <Button
              variant="outline"
              size="sm"
              onClick={() => void read.refetch()}
            >
              Try again
            </Button>
          </Empty>
        ) : files.length === 0 && leftOut.length === 0 ? (
          <Empty>
            <EmptyHeader>
              <EmptyTitle>No changes yet</EmptyTitle>
              <EmptyDescription>
                What the agent changes in{" "}
                {names(sources.map((source) => source.source))} shows here.
              </EmptyDescription>
            </EmptyHeader>
          </Empty>
        ) : (
          <>
            {leftOut.length > 0 && (
              <Alert className="mx-3 mb-3 w-auto">
                <AlertDescription>
                  There's too much to show all of{" "}
                  {names(leftOut.map((source) => source.source))}: its last
                  files are left out. Download the files to see everything.
                </AlertDescription>
              </Alert>
            )}
            <ChangesView files={files} layout={layout} />
          </>
        )}
      </SheetContent>
    </Sheet>
  )
}

function ChangesView({
  files,
  layout,
}: {
  files: readonly ChangedFile[]
  layout: DiffLayout
}) {
  const list = React.useRef<DiffListHandle>(null)
  const [view, setView] = React.useState<DiffListView>({
    current: files[0]?.path ?? "",
    onScreen: files[0] ? [files[0].path] : [],
  })
  const paths = files.map((file) => file.path)
  const statuses = files.map(({ path, status }) => ({ path, status }))

  return (
    <>
      {/* Phones have no room for the tree or a split view. A list of the files jumps to one, and
          the header at the top of the list says which file is on screen. */}
      <NativeSelect
        aria-label="Go to a file"
        value=""
        onChange={(event) => list.current?.showFile(event.target.value)}
        className="mx-3 mb-3 w-auto md:hidden"
      >
        <NativeSelectOption value="" disabled>
          Go to a file
        </NativeSelectOption>
        {files.map((file) => (
          <NativeSelectOption key={file.path} value={file.path}>
            {file.path}
          </NativeSelectOption>
        ))}
      </NativeSelect>
      <div className="flex min-h-0 flex-1 gap-3 px-3 pb-3">
        <FileTree
          paths={paths}
          changes={statuses}
          selected={view.current}
          onScreen={view.onScreen}
          onSelect={(path) => list.current?.showFile(path)}
          className="hidden w-60 shrink-0 md:block"
        />
        <div className="min-w-0 flex-1 overflow-hidden rounded-xl bg-card shadow-raised">
          <DiffList
            ref={list}
            files={files}
            layout={layout}
            onViewChange={setView}
          />
        </div>
      </div>
    </>
  )
}

// The nook's files as they are, all of /work, as an archive the browser saves.
export function DownloadFiles({ nookId }: { nookId: string }) {
  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <a
            href={new URL(`nooks/${nookId}/download`, apiUrl).href}
            download
            aria-label="Download files"
            className={buttonVariants({ variant: "ghost", size: "icon-sm" })}
          />
        }
      >
        <DownloadSimpleIcon />
      </TooltipTrigger>
      <TooltipContent>Download files</TooltipContent>
    </Tooltip>
  )
}

// "api", "api and web", "api, web, and docs".
function names(all: readonly string[]): string {
  if (all.length <= 2) return all.join(" and ")
  return `${all.slice(0, -1).join(", ")}, and ${all.at(-1)}`
}
