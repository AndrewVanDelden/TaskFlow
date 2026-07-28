import { useEffect, useState } from 'react'
import type { TaskItem, TaskStatus } from '../types'
import { getTasks, updateTaskStatus, approveTask } from '../api/tasks'
import { HubEvents } from '../lib/hubEvents'
import { useAgentHub } from '../lib/agentHub'

interface TaskMovedEvent {
  id: number
  status: TaskStatus
}

// Owns the board's task state: one initial load, then live single-card patches from TaskMoved,
// plus an optimistic moveTask for drag-and-drop. This replaces the whole-board refetch that used
// to run on every agent log (finding F2).
export function useBoardTasks() {
  const [tasks, setTasks] = useState<TaskItem[]>([])
  const [error, setError] = useState<string | null>(null)
  const { connection } = useAgentHub()

  // Initial load, once.
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
  }, [])

  // Live: patch only the moved card. Id-scoped, so an unrelated move never disturbs other cards
  // (including a card the user is mid-drag on).
  useEffect(() => {
    if (!connection) return
    const onMoved = (evt: TaskMovedEvent) =>
      setTasks((prev) => prev.map((t) => (t.id === evt.id ? { ...t, status: evt.status } : t)))
    connection.on(HubEvents.TaskMoved, onMoved)
    return () => connection.off(HubEvents.TaskMoved, onMoved)
  }, [connection])

  // One optimistic-update-with-rollback path, shared by drag-moves and approvals (DRY): change the
  // UI immediately, persist, and roll back if the server rejects it.
  const applyOptimistic = async (
    id: number,
    newStatus: TaskStatus,
    persist: () => Promise<unknown>,
    fallback: string,
  ) => {
    const task = tasks.find((t) => t.id === id)
    if (!task || task.status === newStatus) return

    const previous = tasks
    setTasks(tasks.map((t) => (t.id === id ? { ...t, status: newStatus } : t)))

    try {
      await persist()
    } catch (err) {
      setTasks(previous)
      setError(err instanceof Error ? err.message : fallback)
    }
  }

  const moveTask = (id: number, newStatus: TaskStatus) =>
    applyOptimistic(id, newStatus, () => updateTaskStatus(id, newStatus), 'Failed to move task.')

  // Human approval: Review -> Done through the dedicated approve endpoint.
  const approve = (id: number) =>
    applyOptimistic(id, 'Done', () => approveTask(id), 'Failed to approve task.')

  return { tasks, error, moveTask, approve }
}
