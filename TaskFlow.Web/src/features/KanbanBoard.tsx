import { useState } from 'react'
import {
  DndContext,
  DragOverlay,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragStartEvent,
} from '@dnd-kit/core'
import type { AgentLog } from '../types'
import { useBoardTasks } from '../hooks/useBoardTasks'
import { KanbanColumn } from '../components/KanbanColumn'
import { TaskCardView } from '../components/TaskCardView'
import { BOARD_COLUMNS, resolveDropColumn, taskOutput, reviewReadyPairs } from '../lib/board'

export function KanbanBoard({ logs = [] }: { logs?: AgentLog[] }) {
  const { tasks, error, moveTask, approve, reject } = useBoardTasks()
  const [activeId, setActiveId] = useState<number | null>(null)
  const outputFor = (id: number) => taskOutput(logs, id)

  // ReviewReady application pairs, computed once from the visible task list. Their two sibling
  // tasks render as one ApplicationReviewCard in the Review column instead of two TaskCards.
  const pairs = reviewReadyPairs(tasks)
  const pairedTaskIds = new Set(pairs.flatMap((p) => [p.resumeTask.id, p.coverLetterTask.id]))

  // Require a small drag distance before starting, so clicks still work.
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
  )

  const handleDragStart = (event: DragStartEvent) => setActiveId(Number(event.active.id))

  const handleDragEnd = (event: DragEndEvent) => {
    setActiveId(null)
    const { active, over } = event
    if (!over) return
    // over.id may be a column (status) or a card (task id). Resolve to the destination column so a
    // drop onto a card moves the card to that column instead of blanking its status with a task id.
    const destination = resolveDropColumn(over.id, tasks)
    if (!destination) return
    moveTask(Number(active.id), destination)
  }

  const activeTask = activeId != null ? tasks.find((t) => t.id === activeId) ?? null : null

  return (
    <div>
      {error && (
        <div className="mb-3 text-sm text-red-400 bg-red-950 border border-red-900 rounded px-3 py-2">
          {error}
        </div>
      )}

      <DndContext
        sensors={sensors}
        onDragStart={handleDragStart}
        onDragEnd={handleDragEnd}
        onDragCancel={() => setActiveId(null)}
      >
        <div className="flex gap-3 overflow-x-auto pb-2">
          {BOARD_COLUMNS.map((col) => (
            <KanbanColumn
              key={col.status}
              status={col.status}
              label={col.label}
              // Paired tasks render inside ApplicationReviewCard instead, so filter them out of the
              // individual list here — Review-column-only, so no double-rendering. Other columns'
              // tasks are never part of a pair (pairs require both siblings to be Review), but the
              // filter is unconditional so this can never regress if that invariant ever changes.
              tasks={tasks.filter((t) => t.status === col.status && !pairedTaskIds.has(t.id))}
              // Approve/Reject and the executor output are Review-column affordances only.
              onApprove={col.status === 'Review' ? approve : undefined}
              onReject={col.status === 'Review' ? reject : undefined}
              outputFor={col.status === 'Review' ? outputFor : undefined}
              pairs={col.status === 'Review' ? pairs : undefined}
            />
          ))}
        </div>

        {/* The dragged card rides in a portal above the board, so it is not clipped by the
            columns' overflow and is unaffected by live re-renders during the drag. */}
        <DragOverlay>{activeTask ? <TaskCardView task={activeTask} /> : null}</DragOverlay>
      </DndContext>
    </div>
  )
}
