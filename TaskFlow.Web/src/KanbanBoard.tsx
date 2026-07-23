import { useEffect, useState } from 'react'
import {
  DndContext,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core'
import type { TaskItem, TaskStatus } from './types'
import { getTasks, updateTaskStatus } from './api'
import { KanbanColumn } from './KanbanColumn'

const COLUMNS: { status: TaskStatus; label: string }[] = [
  { status: 'Todo', label: 'To Do' },
  { status: 'InProgress', label: 'In Progress' },
  { status: 'Review', label: 'Review' },
  { status: 'Done', label: 'Done' },
]

export function KanbanBoard({ refreshKey }: { refreshKey: number }) {
  const [tasks, setTasks] = useState<TaskItem[]>([])
  const [error, setError] = useState<string | null>(null)

  // Require a small drag distance before starting, so clicks still work.
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
  )

  useEffect(() => {
    let cancelled = false

    getTasks()
      .then((data) => {
        if (cancelled) return
        setTasks(data)
        setError(null)
      })
      .catch((err) => {
        if (cancelled) return
        setError(err instanceof Error ? err.message : 'Failed to load tasks.')
      })

    return () => {
      cancelled = true
    }
  }, [refreshKey])

  const handleDragEnd = async (event: DragEndEvent) => {
    const { active, over } = event
    if (!over) return

    const taskId = Number(active.id)
    const newStatus = over.id as TaskStatus
    const task = tasks.find((t) => t.id === taskId)

    if (!task || task.status === newStatus) return

    // Optimistic update: change the UI immediately, reconcile with the server after.
    const previous = tasks
    setTasks(tasks.map((t) => (t.id === taskId ? { ...t, status: newStatus } : t)))

    try {
      await updateTaskStatus(taskId, newStatus)
    } catch (err) {
      // Roll back if the server rejected it.
      setTasks(previous)
      setError(err instanceof Error ? err.message : 'Failed to move task.')
    }
  }

  return (
    <div>
      {error && (
        <div className="mb-3 text-sm text-red-400 bg-red-950 border border-red-900 rounded px-3 py-2">
          {error}
        </div>
      )}

      <DndContext sensors={sensors} onDragEnd={handleDragEnd}>
        <div className="flex gap-3 overflow-x-auto pb-2">
          {COLUMNS.map((col) => (
            <KanbanColumn
              key={col.status}
              status={col.status}
              label={col.label}
              tasks={tasks.filter((t) => t.status === col.status)}
            />
          ))}
        </div>
      </DndContext>
    </div>
  )
}