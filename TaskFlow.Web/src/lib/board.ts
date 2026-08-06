import type { AgentLog, TaskItem, TaskStatus } from '../types'

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

// The executor's work for a task's CURRENT cycle: progress notes and the review summary (which holds
// the deliverable), oldest first. Scoped to the most recent "Claimed" so earlier runs — and any prior
// task that reused this id after a seed change — do not pile up. Lets the Review card show the output
// inline instead of making the user hunt the feed.
const OUTPUT_ACTIONS = ['ProgressRecorded', 'ReviewRequested', 'AutoFinalized']

export function taskOutput(logs: AgentLog[], taskId: number): string[] {
  const forTask = logs.filter((l) => l.taskId === taskId)

  const latestClaimAt = forTask
    .filter((l) => l.action === 'Claimed')
    .reduce<string | null>((max, l) => (max === null || l.createdAt > max ? l.createdAt : max), null)
  if (latestClaimAt === null) return []

  return forTask
    .filter((l) => !!l.details && OUTPUT_ACTIONS.includes(l.action) && l.createdAt >= latestClaimAt)
    .slice()
    .sort((a, b) => a.createdAt.localeCompare(b.createdAt))
    .map((l) => l.details as string)
}
