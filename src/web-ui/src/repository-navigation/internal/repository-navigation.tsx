import { useCallback, useEffect, useRef, useState } from "react"
import {
  BotIcon,
  ChevronRightIcon,
  FolderGit2Icon,
  FolderKanbanIcon,
  PlusIcon,
  SettingsIcon,
  Undo2Icon,
} from "lucide-react"
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogTitle,
} from "@/components/ui/dialog"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Button } from "@/components/ui/button"
import { Field, FieldError, FieldLabel } from "@/components/ui/field"
import { Input } from "@/components/ui/input"
import {
  SidebarGroup,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from "@/components/ui/sidebar"
import type {
  NewRepositoryItem,
  Project,
  ProjectDetails,
  Repository,
  RepositoryItem as ProjectTreeItem,
  Session,
  RepositoryCatalog,
} from ".."
import { usePagedWindow } from "./use-paged-window"
import { useRepositoryCollections } from "./use-repository-collections"
import { ProjectTreeDragAndDrop } from "./project-tree-drag-and-drop"
import {
  useProjectTreeDraggable,
  useProjectTreeDroppable,
  type ProjectTreeDragItem,
  type ProjectTreeDropTarget,
} from "./project-tree-drag-state"

const MAX_REPOSITORIES = 100
const MAX_COLLECTION_ITEMS = 50
const MAX_STORED_OPEN_NODES = 500
const OPEN_NODES_STORAGE_KEY = "ai-sloth-open-project-tree-nodes"
const UNDO_DURATION_MS = 6000

let storedOpenNodes: Set<string> | null = null

type CreateTarget = {
  kind: ProjectTreeItem["kind"]
  parentProjectId: string | null
}

type UndoMove = {
  id: number
  item: ProjectTreeDragItem
  sourceProjectId: string | null
  targetProjectId: string | null
}

type RepositoryCollections = ReturnType<typeof useRepositoryCollections>

export function RepositoryNavigation({
  catalog,
  workspaceId,
  onSelectSession,
}: {
  catalog: RepositoryCatalog
  workspaceId: string
  onSelectSession(sessionId: string): void
}) {
  const loadPage = useCallback(
    (cursor: string | null, signal: AbortSignal) =>
      catalog.listRepositories(workspaceId, cursor, signal),
    [catalog, workspaceId]
  )
  const repositories = usePagedWindow(loadPage, MAX_REPOSITORIES)

  return (
    <SidebarGroup>
      <SidebarGroupLabel>Repositories</SidebarGroupLabel>
      <SidebarMenu>
        <CollectionSentinel
          enabled={
            repositories.hasPrevious &&
            !repositories.loading &&
            !repositories.error
          }
          onVisible={repositories.loadPrevious}
        />
        {repositories.items.map((repository) => (
          <RepositoryRow
            key={repository.id}
            catalog={catalog}
            workspaceId={workspaceId}
            repository={repository}
            onSelectSession={onSelectSession}
          />
        ))}
        <CollectionStatus
          empty={repositories.items.length === 0}
          loading={repositories.loading}
          error={repositories.error}
          loadingLabel="Loading repositories…"
          emptyLabel="No repositories yet."
          retry={repositories.retry}
        />
        <CollectionSentinel
          enabled={
            repositories.hasNext && !repositories.loading && !repositories.error
          }
          onVisible={repositories.loadNext}
        />
      </SidebarMenu>
    </SidebarGroup>
  )
}

function RepositoryRow({
  catalog,
  workspaceId,
  repository,
  onSelectSession,
}: {
  catalog: RepositoryCatalog
  workspaceId: string
  repository: Repository
  onSelectSession(sessionId: string): void
}) {
  const storageId = `${workspaceId}:${repository.id}`
  const [open, setOpenState] = useState(() => isNodeStoredOpen(storageId))
  const setOpen = useCallback(
    (open: boolean) => {
      setOpenState(open)
      storeNodeOpen(storageId, open)
    },
    [storageId]
  )
  const collections = useRepositoryCollections(
    catalog,
    workspaceId,
    repository.id,
    MAX_COLLECTION_ITEMS
  )
  const [createTarget, setCreateTarget] = useState<CreateTarget | null>(null)
  const [editingProject, setEditingProject] = useState<ProjectDetails | null>(
    null
  )
  const [undoMove, setUndoMove] = useState<UndoMove | null>(null)
  const [error, setError] = useState<string | null>(null)
  const activeOperation = useRef<AbortController | null>(null)
  const clearUndoMove = useCallback(() => setUndoMove(null), [])
  const nextUndoId = useRef(0)

  useEffect(
    () => () => {
      activeOperation.current?.abort()
    },
    []
  )

  async function moveItem(
    item: ProjectTreeDragItem,
    targetProjectId: string | null,
    offerUndo = true
  ) {
    if (item.sourceProjectId === targetProjectId) return true
    activeOperation.current?.abort()
    const controller = new AbortController()
    activeOperation.current = controller
    setError(null)

    try {
      await catalog.moveItem(
        workspaceId,
        repository.id,
        { kind: item.kind, id: item.id },
        targetProjectId,
        controller.signal
      )
      if (activeOperation.current === controller) {
        collections.invalidate(item.sourceProjectId, targetProjectId)
        if (offerUndo) {
          nextUndoId.current += 1
          setUndoMove({
            id: nextUndoId.current,
            item,
            sourceProjectId: item.sourceProjectId,
            targetProjectId,
          })
        }
      }
      return true
    } catch (cause) {
      if (!controller.signal.aborted) {
        setError(cause instanceof Error ? cause.message : "Could not move item")
      }
      return false
    } finally {
      if (activeOperation.current === controller) {
        activeOperation.current = null
      }
    }
  }

  async function undoLastMove() {
    if (!undoMove) return false
    const succeeded = await moveItem(
      { ...undoMove.item, sourceProjectId: undoMove.targetProjectId },
      undoMove.sourceProjectId,
      false
    )
    if (succeeded) setUndoMove(null)
    return succeeded
  }

  async function createItem(input: NewRepositoryItem) {
    if (!createTarget || input.kind !== createTarget.kind) return false
    activeOperation.current?.abort()
    const controller = new AbortController()
    activeOperation.current = controller
    setError(null)

    try {
      const created = await catalog.createItem(
        workspaceId,
        repository.id,
        createTarget.parentProjectId,
        input,
        controller.signal
      )
      if (activeOperation.current === controller) {
        if (created) onSelectSession(created.id)
        collections.invalidate(createTarget.parentProjectId)
        setCreateTarget(null)
      }
      return true
    } catch (cause) {
      if (!controller.signal.aborted) {
        setError(
          cause instanceof Error ? cause.message : "Could not create item"
        )
      }
      return false
    } finally {
      if (activeOperation.current === controller) {
        activeOperation.current = null
      }
    }
  }

  async function openProjectSettings(project: Project) {
    activeOperation.current?.abort()
    const controller = new AbortController()
    activeOperation.current = controller
    setError(null)
    try {
      const details = await catalog.getProject(
        workspaceId,
        repository.id,
        project.id,
        controller.signal
      )
      if (activeOperation.current === controller) setEditingProject(details)
    } catch (cause) {
      if (!controller.signal.aborted) {
        setError(
          cause instanceof Error ? cause.message : "Could not load project"
        )
      }
    } finally {
      if (activeOperation.current === controller) activeOperation.current = null
    }
  }

  async function saveProject(input: { name: string; instructions: string }) {
    if (!editingProject) return false
    activeOperation.current?.abort()
    const controller = new AbortController()
    activeOperation.current = controller
    setError(null)
    try {
      await catalog.updateProject(
        workspaceId,
        repository.id,
        editingProject.id,
        { ...input, expectedVersion: editingProject.version },
        controller.signal
      )
      if (activeOperation.current === controller) {
        collections.invalidate(editingProject.parentProjectId)
        setEditingProject(null)
      }
      return true
    } catch (cause) {
      if (!controller.signal.aborted) {
        setError(
          cause instanceof Error ? cause.message : "Could not update project"
        )
      }
      return false
    } finally {
      if (activeOperation.current === controller) activeOperation.current = null
    }
  }

  return (
    <SidebarMenuItem>
      <ProjectTreeDragAndDrop
        onMove={(item, target) => void moveItem(item, target.projectId)}
        renderOverlay={(item) => <DragOverlayItem item={item} />}
      >
        <RepositoryTree
          collections={collections}
          workspaceId={workspaceId}
          repository={repository}
          open={open}
          error={error}
          onOpenChange={setOpen}
          onCreate={(kind, parentProjectId) =>
            setCreateTarget({ kind, parentProjectId })
          }
          onEditProject={(project) => void openProjectSettings(project)}
          onSelectSession={onSelectSession}
        />
        <CreateItemDialog
          target={createTarget}
          defaultBranch={repository.defaultBranch}
          onClose={() => setCreateTarget(null)}
          onCreate={createItem}
        />
        <ProjectSettingsDialog
          project={editingProject}
          onClose={() => setEditingProject(null)}
          onSave={saveProject}
        />
        {undoMove && (
          <UndoMoveNotice
            key={undoMove.id}
            itemName={undoMove.item.name}
            onExpire={clearUndoMove}
            onUndo={undoLastMove}
          />
        )}
      </ProjectTreeDragAndDrop>
    </SidebarMenuItem>
  )
}

function RepositoryTree({
  collections,
  workspaceId,
  repository,
  open,
  error,
  onOpenChange,
  onCreate,
  onEditProject,
  onSelectSession,
}: {
  collections: RepositoryCollections
  workspaceId: string
  repository: Repository
  open: boolean
  error: string | null
  onOpenChange(open: boolean): void
  onCreate(kind: ProjectTreeItem["kind"], parentProjectId: string | null): void
  onEditProject(project: Project): void
  onSelectSession(sessionId: string): void
}) {
  const openRepository = useCallback(() => onOpenChange(true), [onOpenChange])
  const {
    setNodeRef: setRootDropRef,
    isMoveTarget: isRootMoveTarget,
    activeItem: activeDragItem,
  } = useProjectTreeDroppable(
    {
      projectId: null,
      projectName: null,
      depth: -1,
      ancestorProjectIds: [],
    },
    open ? undefined : openRepository
  )

  return (
    <Collapsible
      open={open}
      onOpenChange={onOpenChange}
      className={
        isRootMoveTarget
          ? "group/repository bg-sidebar-accent/50"
          : "group/repository"
      }
      render={<div ref={setRootDropRef} />}
    >
      <div className="relative">
        <CollapsibleTrigger
          render={
            <SidebarMenuButton
              className="pr-14"
              tooltip={`${repository.owner}/${repository.name}`}
            />
          }
        >
          <FolderGit2Icon />
          <span>{repository.name}</span>
          <ChevronRightIcon className="ml-auto transition-transform group-data-open/repository:rotate-90" />
        </CollapsibleTrigger>
        <CreateMenu
          className="absolute top-1.5 right-1"
          onCreate={(kind) => onCreate(kind, null)}
        />
      </div>
      {open && (
        <CollapsibleContent>
          {error && (
            <p className="mx-4 my-1 text-xs text-destructive">{error}</p>
          )}
          {isRootMoveTarget && activeDragItem && (
            <DropPreview item={activeDragItem} />
          )}
          <ItemCollection
            collections={collections}
            workspaceId={workspaceId}
            repositoryId={repository.id}
            parentProjectId={null}
            ancestorProjectIds={[]}
            onCreate={onCreate}
            onEditProject={onEditProject}
            onSelectSession={onSelectSession}
          />
        </CollapsibleContent>
      )}
    </Collapsible>
  )
}

function ItemCollection({
  collections,
  workspaceId,
  repositoryId,
  parentProjectId,
  ancestorProjectIds,
  onCreate,
  onEditProject,
  onSelectSession,
}: {
  collections: RepositoryCollections
  workspaceId: string
  repositoryId: string
  parentProjectId: string | null
  ancestorProjectIds: readonly string[]
  onCreate(kind: ProjectTreeItem["kind"], parentProjectId: string | null): void
  onEditProject(project: Project): void
  onSelectSession(sessionId: string): void
}) {
  const { activate, loadNext, loadPrevious, retry } = collections
  const state =
    collections.collections.get(parentProjectId) ?? collections.emptyCollection
  const items = state.pages.flatMap((page) => page.items)
  const hasPrevious = Boolean(state.pages[0]?.previousCursor)
  const hasNext = Boolean(state.pages.at(-1)?.nextCursor)
  const loadNextPage = useCallback(
    () => loadNext(parentProjectId),
    [loadNext, parentProjectId]
  )
  const loadPreviousPage = useCallback(
    () => loadPrevious(parentProjectId),
    [loadPrevious, parentProjectId]
  )
  const retryLoad = useCallback(
    () => retry(parentProjectId),
    [parentProjectId, retry]
  )

  useEffect(() => activate(parentProjectId), [activate, parentProjectId])

  return (
    <ul className="mx-3.5 min-w-0 border-l border-sidebar-border px-2.5 py-0.5">
      <CollectionSentinel
        enabled={hasPrevious && !state.loading && !state.error}
        onVisible={loadPreviousPage}
      />
      {items.map((item) =>
        item.kind === "project" ? (
          <ProjectRow
            key={item.id}
            project={item}
            collections={collections}
            workspaceId={workspaceId}
            repositoryId={repositoryId}
            parentProjectId={parentProjectId}
            ancestorProjectIds={ancestorProjectIds}
            onCreate={onCreate}
            onEditProject={onEditProject}
            onSelectSession={onSelectSession}
          />
        ) : (
          <SessionRow
            key={item.id}
            session={item}
            parentProjectId={parentProjectId}
            onSelect={onSelectSession}
          />
        )
      )}
      <CollectionStatus
        empty={items.length === 0}
        loading={state.loading}
        error={state.error}
        loadingLabel="Loading…"
        emptyLabel="No sessions or projects."
        retry={retryLoad}
      />
      <CollectionSentinel
        enabled={hasNext && !state.loading && !state.error}
        onVisible={loadNextPage}
      />
    </ul>
  )
}

function ProjectRow({
  project,
  collections,
  workspaceId,
  repositoryId,
  parentProjectId,
  ancestorProjectIds,
  onCreate,
  onEditProject,
  onSelectSession,
}: {
  project: Project
  collections: RepositoryCollections
  workspaceId: string
  repositoryId: string
  parentProjectId: string | null
  ancestorProjectIds: readonly string[]
  onCreate(kind: ProjectTreeItem["kind"], parentProjectId: string | null): void
  onEditProject(project: Project): void
  onSelectSession(sessionId: string): void
}) {
  const storageId = `${workspaceId}:${repositoryId}:${project.id}`
  const [open, setOpenState] = useState(() => isNodeStoredOpen(storageId))
  const setProjectOpen = useCallback(
    (open: boolean) => {
      setOpenState(open)
      storeNodeOpen(storageId, open)
    },
    [storageId]
  )
  const openProject = useCallback(() => setProjectOpen(true), [setProjectOpen])
  const dragItem = {
    ...project,
    sourceProjectId: parentProjectId,
  } satisfies ProjectTreeDragItem
  const {
    attributes,
    listeners,
    setNodeRef: setDragNodeRef,
    isDragging,
  } = useProjectTreeDraggable(dragItem)
  const dropTarget = {
    projectId: project.id,
    projectName: project.name,
    depth: ancestorProjectIds.length,
    ancestorProjectIds,
  } satisfies ProjectTreeDropTarget
  const {
    setNodeRef: setDropNodeRef,
    isMoveTarget,
    activeItem,
  } = useProjectTreeDroppable(dropTarget, open ? undefined : openProject)
  const setRowRef = useCallback(
    (element: HTMLLIElement | null) => {
      setDragNodeRef(element)
      setDropNodeRef(element)
    },
    [setDragNodeRef, setDropNodeRef]
  )

  return (
    <li
      ref={setRowRef}
      className={isMoveTarget ? "bg-sidebar-accent/60" : undefined}
    >
      <div
        className={`group/project-row relative flex h-7 min-w-0 items-center ${isDragging ? "opacity-25" : ""}`}
      >
        <button
          type="button"
          className="flex h-7 min-w-0 flex-1 items-center gap-2 py-0 pr-8 pl-2 text-left text-xs hover:bg-sidebar-accent"
          onClick={() => setProjectOpen(!open)}
          {...attributes}
          {...listeners}
        >
          <FolderKanbanIcon className="size-3.5 shrink-0" />
          <span className="truncate">{project.name}</span>
          <ChevronRightIcon
            className={`ml-auto size-3.5 shrink-0 transition-transform ${open ? "rotate-90" : ""}`}
          />
        </button>
        <button
          type="button"
          className="absolute top-1 right-6 flex size-5 items-center justify-center opacity-0 group-hover/project-row:opacity-100 hover:bg-sidebar-accent focus:opacity-100"
          title="Project settings"
          onClick={() => onEditProject(project)}
        >
          <SettingsIcon className="size-3.5" />
          <span className="sr-only">Project settings</span>
        </button>
        <CreateMenu
          className="absolute top-1 right-1 opacity-0 group-hover/project-row:opacity-100 focus-within:opacity-100"
          onCreate={(kind) => onCreate(kind, project.id)}
        />
      </div>
      {isMoveTarget && activeItem && <DropPreview item={activeItem} />}
      {open && (
        <ItemCollection
          collections={collections}
          workspaceId={workspaceId}
          repositoryId={repositoryId}
          parentProjectId={project.id}
          ancestorProjectIds={[...ancestorProjectIds, project.id]}
          onCreate={onCreate}
          onEditProject={onEditProject}
          onSelectSession={onSelectSession}
        />
      )}
    </li>
  )
}

function SessionRow({
  session,
  parentProjectId,
  onSelect,
}: {
  session: Session
  parentProjectId: string | null
  onSelect(sessionId: string): void
}) {
  const dragItem = {
    ...session,
    sourceProjectId: parentProjectId,
  } satisfies ProjectTreeDragItem
  const { attributes, listeners, setNodeRef, isDragging } =
    useProjectTreeDraggable(dragItem)

  return (
    <li
      ref={setNodeRef}
      className={`group/session relative flex h-7 min-w-0 items-center text-xs text-sidebar-foreground ${isDragging ? "opacity-25" : ""}`}
    >
      <button
        type="button"
        className="flex h-7 min-w-0 flex-1 items-center gap-2 px-2 text-left"
        onClick={() => onSelect(session.id)}
        {...attributes}
        {...listeners}
      >
        <BotIcon className="size-3.5 shrink-0" />
        <span className="truncate">{session.name}</span>
      </button>
      <SessionStatus status={session.status} />
    </li>
  )
}

function DragOverlayItem({ item }: { item: ProjectTreeDragItem }) {
  return (
    <div className="flex h-8 w-64 max-w-[calc(100vw-2rem)] items-center gap-2 border border-sidebar-border bg-sidebar px-2 text-xs text-sidebar-foreground shadow-lg">
      <ProjectTreeItemIcon kind={item.kind} />
      <span className="truncate">{item.name}</span>
      {item.kind === "session" && <SessionStatus status={item.status} />}
    </div>
  )
}

function DropPreview({ item }: { item: ProjectTreeDragItem }) {
  return (
    <div className="my-0.5 mr-2 ml-6 flex h-7 min-w-0 items-center gap-2 border border-dashed border-sidebar-ring bg-sidebar-accent px-2 text-xs text-sidebar-foreground">
      <ProjectTreeItemIcon kind={item.kind} />
      <span className="truncate">{item.name}</span>
      {item.kind === "session" && <SessionStatus status={item.status} />}
    </div>
  )
}

function ProjectTreeItemIcon({ kind }: { kind: ProjectTreeItem["kind"] }) {
  return kind === "project" ? (
    <FolderKanbanIcon className="size-3.5 shrink-0" />
  ) : (
    <BotIcon className="size-3.5 shrink-0" />
  )
}

function UndoMoveNotice({
  itemName,
  onExpire,
  onUndo,
}: {
  itemName: string
  onExpire(): void
  onUndo(): Promise<boolean>
}) {
  const [pending, setPending] = useState(false)

  useEffect(() => {
    if (pending) return
    const timer = window.setTimeout(onExpire, UNDO_DURATION_MS)
    return () => window.clearTimeout(timer)
  }, [onExpire, pending])

  async function undo() {
    if (pending) return
    setPending(true)
    if (!(await onUndo())) setPending(false)
  }

  return (
    <div
      role="status"
      className="fixed right-4 bottom-4 z-50 flex max-w-sm items-center gap-3 border bg-popover px-3 py-2 text-sm text-popover-foreground shadow-lg"
    >
      <span className="truncate">Moved {itemName}</span>
      <Button variant="ghost" size="sm" disabled={pending} onClick={undo}>
        <Undo2Icon />
        {pending ? "Undoing…" : "Undo"}
      </Button>
    </div>
  )
}

function CreateMenu({
  className,
  onCreate,
}: {
  className?: string
  onCreate(kind: ProjectTreeItem["kind"]): void
}) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={
          <button
            type="button"
            className={`flex size-5 items-center justify-center hover:bg-sidebar-accent ${className ?? ""}`}
            title="Create"
          />
        }
      >
        <PlusIcon className="size-3.5" />
        <span className="sr-only">Create</span>
      </DropdownMenuTrigger>
      <DropdownMenuContent side="right" align="start" className="min-w-40">
        <DropdownMenuItem onClick={() => onCreate("session")}>
          <BotIcon />
          New session
        </DropdownMenuItem>
        <DropdownMenuItem onClick={() => onCreate("project")}>
          <FolderKanbanIcon />
          New project
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

function CreateItemDialog({
  target,
  defaultBranch,
  onClose,
  onCreate,
}: {
  target: CreateTarget | null
  defaultBranch: string
  onClose(): void
  onCreate(input: NewRepositoryItem): Promise<boolean>
}) {
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (pending || !target) return

    setError(null)
    setPending(true)
    const data = new FormData(event.currentTarget)
    const name = String(data.get("name") ?? "")
    const input: NewRepositoryItem =
      target.kind === "project"
        ? { kind: "project", name }
        : {
            kind: "session",
            name,
            branch: String(data.get("branch") ?? ""),
            prompt: String(data.get("prompt") ?? ""),
          }
    if (!(await onCreate(input))) setError(`Could not create ${target.kind}`)
    setPending(false)
  }

  return (
    <Dialog open={target !== null} onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogTitle>
          New {target?.kind === "project" ? "project" : "session"}
        </DialogTitle>
        <DialogDescription>
          {target?.parentProjectId
            ? "Create it inside this project."
            : "Create it at the repository root."}
        </DialogDescription>
        <form className="space-y-4" onSubmit={submit}>
          <Field>
            <FieldLabel htmlFor="repository-item-name">Name</FieldLabel>
            <Input
              id="repository-item-name"
              name="name"
              maxLength={100}
              autoFocus
              disabled={pending}
              required
            />
          </Field>
          {target?.kind === "session" && (
            <>
              <Field>
                <FieldLabel htmlFor="session-branch">Branch</FieldLabel>
                <Input
                  id="session-branch"
                  name="branch"
                  defaultValue={defaultBranch}
                  maxLength={255}
                  disabled={pending}
                  required
                />
              </Field>
              <Field>
                <FieldLabel htmlFor="session-prompt">Prompt</FieldLabel>
                <textarea
                  id="session-prompt"
                  name="prompt"
                  className="min-h-32 w-full border bg-transparent px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring"
                  maxLength={16 * 1024}
                  disabled={pending}
                  required
                />
              </Field>
            </>
          )}
          {error && <FieldError>{error}</FieldError>}
          <Button type="submit" className="w-full" disabled={pending}>
            {pending ? "Creating…" : `Create ${target?.kind ?? "item"}`}
          </Button>
        </form>
      </DialogContent>
    </Dialog>
  )
}

function ProjectSettingsDialog({
  project,
  onClose,
  onSave,
}: {
  project: ProjectDetails | null
  onClose(): void
  onSave(input: { name: string; instructions: string }): Promise<boolean>
}) {
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (pending) return
    const data = new FormData(event.currentTarget)
    setError(null)
    setPending(true)
    const saved = await onSave({
      name: String(data.get("name") ?? ""),
      instructions: String(data.get("instructions") ?? ""),
    })
    if (!saved) setError("Could not update project")
    setPending(false)
  }

  return (
    <Dialog open={project !== null} onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogTitle>Project settings</DialogTitle>
        <DialogDescription>
          Instructions are inherited by sessions in this project and its nested
          projects.
        </DialogDescription>
        {project && (
          <form key={project.id} className="space-y-4" onSubmit={submit}>
            <Field>
              <FieldLabel htmlFor="project-name">Name</FieldLabel>
              <Input
                id="project-name"
                name="name"
                defaultValue={project.name}
                maxLength={100}
                disabled={pending}
                required
              />
            </Field>
            <Field>
              <FieldLabel htmlFor="project-instructions">
                Instructions
              </FieldLabel>
              <textarea
                id="project-instructions"
                name="instructions"
                defaultValue={project.instructions}
                className="min-h-48 w-full border bg-transparent px-3 py-2 font-mono text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring"
                maxLength={16 * 1024}
                disabled={pending}
              />
            </Field>
            {error && <FieldError>{error}</FieldError>}
            <Button type="submit" className="w-full" disabled={pending}>
              {pending ? "Saving…" : "Save project"}
            </Button>
          </form>
        )}
      </DialogContent>
    </Dialog>
  )
}

function SessionStatus({ status }: { status: Session["status"] }) {
  return (
    <span
      className={
        status === "running"
          ? "ml-auto size-1.5 shrink-0 bg-emerald-500"
          : status === "waiting"
            ? "ml-auto size-1.5 shrink-0 bg-amber-500"
            : status === "failed"
              ? "ml-auto size-1.5 shrink-0 bg-destructive"
              : "ml-auto size-1.5 shrink-0 bg-muted-foreground/40"
      }
      title={status}
    />
  )
}

function isNodeStoredOpen(nodeId: string) {
  return openNodeIds().has(nodeId)
}

function storeNodeOpen(nodeId: string, open: boolean) {
  const nodeIds = openNodeIds()
  nodeIds.delete(nodeId)
  if (open) nodeIds.add(nodeId)
  while (nodeIds.size > MAX_STORED_OPEN_NODES) {
    const oldest = nodeIds.values().next().value
    if (oldest === undefined) break
    nodeIds.delete(oldest)
  }

  try {
    localStorage.setItem(OPEN_NODES_STORAGE_KEY, JSON.stringify([...nodeIds]))
  } catch {
    // Expansion still works when browser storage is unavailable.
  }
}

function openNodeIds() {
  if (storedOpenNodes) return storedOpenNodes

  try {
    const parsed: unknown = JSON.parse(
      localStorage.getItem(OPEN_NODES_STORAGE_KEY) ?? "[]"
    )
    const nodeIds = Array.isArray(parsed)
      ? parsed.filter((value): value is string => typeof value === "string")
      : []
    storedOpenNodes = new Set(nodeIds.slice(-MAX_STORED_OPEN_NODES))
  } catch {
    storedOpenNodes = new Set()
  }
  return storedOpenNodes
}

function CollectionSentinel({
  enabled,
  onVisible,
}: {
  enabled: boolean
  onVisible(): void
}) {
  const sentinel = useRef<HTMLLIElement>(null)

  useEffect(() => {
    const element = sentinel.current
    if (!enabled || !element) return

    const observer = new IntersectionObserver(
      ([entry]) => {
        if (entry?.isIntersecting) onVisible()
      },
      { rootMargin: "96px 0px" }
    )
    observer.observe(element)
    return () => observer.disconnect()
  }, [enabled, onVisible])

  return <li ref={sentinel} className="h-px" aria-hidden="true" />
}

function CollectionStatus({
  empty,
  loading,
  error,
  loadingLabel,
  emptyLabel,
  retry,
}: {
  empty: boolean
  loading: boolean
  error: string | null
  loadingLabel: string
  emptyLabel: string
  retry(): void
}) {
  if (error) {
    return (
      <li>
        <button
          type="button"
          className="w-full px-2 py-1 text-left text-xs text-destructive hover:bg-sidebar-accent"
          onClick={retry}
        >
          {error}. Retry
        </button>
      </li>
    )
  }
  if (!loading && !empty) return null
  return (
    <li className="px-2 py-1 text-xs text-sidebar-foreground/60">
      {loading ? loadingLabel : emptyLabel}
    </li>
  )
}
