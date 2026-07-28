import type { TaskItem, TaskStatus } from '../types'

// Single source of truth for the board's columns (used by the board render and the drop resolver).
export const BOARD_COLUMNS: { status: TaskStatus; label: string }[] = [
  { status: 'Todo', label: 'To Do' },
  { status: 'InProgress', label: 'In Progress' },
  { status: 'Review', label: 'Review' },
  { status: 'Done', label: 'Done' },
]

const STATUSES = BOARD_COLUMNS.map((c) => c.status)

// A drop target is either a column (dnd-kit `over.id` is a status string) or a card (over.id is a
// task id number, because cards are sortable). Resolve either to the destination column's status,
// so dropping onto a card sets the right status instead of a task id (which would blank the card).
// Returns null for an unknown target.
export function resolveDropColumn(overId: string | number, tasks: TaskItem[]): TaskStatus | null {
  if (STATUSES.includes(overId as TaskStatus)) return overId as TaskStatus
  const overTask = tasks.find((t) => t.id === Number(overId))
  return overTask ? overTask.status : null
}
