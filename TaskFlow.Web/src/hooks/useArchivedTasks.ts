import { useEffect, useState } from 'react'
import type { TaskItem } from '../types'
import { getTasks, unarchiveTask } from '../api/tasks'

// Owns the Archive view's task state: one initial load of archived tasks, plus an optimistic
// restore (unarchive) with rollback, mirroring useBoardTasks' own optimistic-with-rollback shape.
export function useArchivedTasks() {
  const [tasks, setTasks] = useState<TaskItem[]>([])
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    getTasks(true)
      .then((data) => {
        if (cancelled) return
        setTasks(data)
        setError(null)
      })
      .catch((err) => {
        if (cancelled) return
        setError(err instanceof Error ? err.message : 'Failed to load archived tasks.')
      })
    return () => {
      cancelled = true
    }
  }, [])

  // Restoring removes the task from the visible archived list immediately, and rolls back if the
  // server rejects it - same optimistic-with-rollback shape as useBoardTasks.
  const restore = async (id: number) => {
    const previous = tasks
    setTasks(tasks.filter((t) => t.id !== id))

    try {
      await unarchiveTask(id)
    } catch (err) {
      setTasks(previous)
      setError(err instanceof Error ? err.message : 'Failed to restore task.')
    }
  }

  return { tasks, error, restore }
}
