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

export interface ApplicationPair {
  applicationId: number
  resumeTask: TaskItem
  coverLetterTask: TaskItem
}

// A ReviewReady application, derived purely from the visible task list rather than a separate
// application-state field: the backend's ApplicationState.ReviewReady is exactly and only true when
// both sibling TaskItems are Review (Sprint 3R's own invariant), so checking both siblings' own
// status here is equivalent and needs no extra field on the wire.
export function reviewReadyPairs(tasks: TaskItem[]): ApplicationPair[] {
  const byApplication = new Map<number, TaskItem[]>()
  for (const task of tasks) {
    if (task.applicationId === null) continue
    const siblings = byApplication.get(task.applicationId) ?? []
    siblings.push(task)
    byApplication.set(task.applicationId, siblings)
  }

  const pairs: ApplicationPair[] = []
  for (const [applicationId, siblings] of byApplication) {
    const resumeTask = siblings.find((t) => t.kind === 'ResumeTailoring' && t.status === 'Review')
    const coverLetterTask = siblings.find(
      (t) => t.kind === 'CoverLetterTailoring' && t.status === 'Review',
    )
    if (resumeTask && coverLetterTask) {
      pairs.push({ applicationId, resumeTask, coverLetterTask })
    }
  }

  return pairs
}
