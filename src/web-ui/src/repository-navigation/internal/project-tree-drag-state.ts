import {
  pointerWithin,
  rectIntersection,
  useDraggable,
  useDroppable,
  type Collision,
  type CollisionDetection,
} from "@dnd-kit/core"
import { createContext, useContext, useEffect } from "react"
import type { RepositoryItem } from ".."

export type ProjectTreeDragItem = RepositoryItem & {
  sourceProjectId: string | null
}

export type ProjectTreeDropTarget = {
  projectId: string | null
  projectName: string | null
  depth: number
  ancestorProjectIds: readonly string[]
}

export const ProjectTreeDragContext = createContext<
  ProjectTreeDragItem | null | undefined
>(undefined)

export function useProjectTreeDraggable(item: ProjectTreeDragItem) {
  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({
    id: `project-tree-item:${item.id}`,
    data: { type: "project-tree-item", item },
  })

  return { attributes, listeners, setNodeRef, isDragging }
}

export function useProjectTreeDroppable(
  target: ProjectTreeDropTarget,
  openAfterHover?: () => void
) {
  const activeItem = useContext(ProjectTreeDragContext)
  if (activeItem === undefined) {
    throw new Error(
      "Project tree drag hooks must be used within ProjectTreeDragAndDrop"
    )
  }

  const { setNodeRef, isOver } = useDroppable({
    id: `project-tree-target:${target.projectId ?? "root"}`,
    data: { type: "project-tree-target", target },
  })
  const canMoveHere =
    isOver &&
    activeItem !== null &&
    activeItem.sourceProjectId !== target.projectId &&
    validTarget(activeItem, target)

  useEffect(() => {
    if (!canMoveHere || !openAfterHover) return
    const timer = window.setTimeout(openAfterHover, 1000)
    return () => window.clearTimeout(timer)
  }, [canMoveHere, openAfterHover])

  return { setNodeRef, isMoveTarget: canMoveHere, activeItem }
}

export const projectTreeCollisionDetection: CollisionDetection = (
  arguments_
) => {
  const collisions = pointerWithin(arguments_)
  const candidates = collisions.length
    ? collisions
    : rectIntersection(arguments_)
  if (!candidates.length) return []

  const deepest = candidates.reduce<Collision | null>((selected, collision) => {
    const selectedDepth = selected
      ? (readDropTarget(
          arguments_.droppableContainers.find(
            (container) => container.id === selected.id
          )?.data.current
        )?.depth ?? -Infinity)
      : -Infinity
    const collisionDepth =
      readDropTarget(
        arguments_.droppableContainers.find(
          (container) => container.id === collision.id
        )?.data.current
      )?.depth ?? -Infinity
    return collisionDepth > selectedDepth ? collision : selected
  }, null)
  if (!deepest) return []

  const item = readDragItem(arguments_.active.data.current)
  const target = readDropTarget(
    arguments_.droppableContainers.find(
      (container) => container.id === deepest.id
    )?.data.current
  )
  return item && target && validTarget(item, target) ? [deepest] : []
}

export function readDragItem(data: Record<string, unknown> | undefined) {
  return data?.type === "project-tree-item"
    ? (data.item as ProjectTreeDragItem)
    : null
}

export function readDropTarget(data: Record<string, unknown> | undefined) {
  return data?.type === "project-tree-target"
    ? (data.target as ProjectTreeDropTarget)
    : null
}

function validTarget(
  item: ProjectTreeDragItem,
  target: ProjectTreeDropTarget
): boolean {
  return !(
    item.kind === "project" &&
    (target.projectId === item.id ||
      target.ancestorProjectIds.includes(item.id))
  )
}
