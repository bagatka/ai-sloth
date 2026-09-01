import {
  DndContext,
  DragOverlay,
  KeyboardSensor,
  MouseSensor,
  TouchSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragStartEvent,
} from "@dnd-kit/core"
import { useState, type ReactNode } from "react"
import { createPortal } from "react-dom"
import {
  ProjectTreeDragContext,
  readDragItem,
  readDropTarget,
  projectTreeCollisionDetection,
  type ProjectTreeDragItem,
  type ProjectTreeDropTarget,
} from "./project-tree-drag-state"

export function ProjectTreeDragAndDrop({
  children,
  onMove,
  renderOverlay,
}: {
  children: ReactNode
  onMove(item: ProjectTreeDragItem, target: ProjectTreeDropTarget): void
  renderOverlay(item: ProjectTreeDragItem): ReactNode
}) {
  const [activeItem, setActiveItem] = useState<ProjectTreeDragItem | null>(null)
  const sensors = useSensors(
    useSensor(MouseSensor, { activationConstraint: { distance: 4 } }),
    useSensor(TouchSensor, {
      activationConstraint: { delay: 180, tolerance: 6 },
    }),
    useSensor(KeyboardSensor)
  )

  function start(event: DragStartEvent) {
    setActiveItem(readDragItem(event.active.data.current))
  }

  function finish(event: DragEndEvent) {
    const item = readDragItem(event.active.data.current)
    const target = readDropTarget(event.over?.data.current)
    setActiveItem(null)
    if (item && target && item.sourceProjectId !== target.projectId) {
      onMove(item, target)
    }
  }

  return (
    <ProjectTreeDragContext.Provider value={activeItem}>
      <DndContext
        sensors={sensors}
        collisionDetection={projectTreeCollisionDetection}
        onDragStart={start}
        onDragEnd={finish}
        onDragCancel={() => setActiveItem(null)}
        accessibility={{
          announcements: {
            onDragStart({ active }) {
              const item = readDragItem(active.data.current)
              return item ? `Picked up ${item.name}.` : "Picked up item."
            },
            onDragOver({ over }) {
              const target = readDropTarget(over?.data.current)
              return target
                ? target.projectName
                  ? `Move into ${target.projectName}.`
                  : "Move to the repository root."
                : "Not over a valid destination."
            },
            onDragEnd({ active, over }) {
              const item = readDragItem(active.data.current)
              const target = readDropTarget(over?.data.current)
              return item && target
                ? `${item.name} moved ${target.projectName ? `into ${target.projectName}` : "to the repository root"}.`
                : "Move cancelled."
            },
            onDragCancel() {
              return "Move cancelled."
            },
          },
        }}
      >
        {children}
        {typeof document !== "undefined" &&
          createPortal(
            <DragOverlay dropAnimation={null} zIndex={100}>
              {activeItem ? renderOverlay(activeItem) : null}
            </DragOverlay>,
            document.body
          )}
      </DndContext>
    </ProjectTreeDragContext.Provider>
  )
}
