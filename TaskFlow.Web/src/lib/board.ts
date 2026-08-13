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

export type TaskStage = 'pending' | 'in-progress' | 'saved' | 'rolled-back'

// Per-item progress for one Epic 3 tailoring task (resume or cover letter), derived from the same
// AgentLog feed the board already consumes - no new SignalR event type needed. Scoped to the most
// recent 'Claimed' entry for this task id, mirroring taskOutput's own "current cycle" scoping, so a
// stale prior cycle (e.g. before a rollback-and-retry) never reports as the current state.
export function taskStage(logs: AgentLog[], taskId: number): TaskStage {
  const forTask = logs.filter((l) => l.taskId === taskId)

  const latestClaimAt = forTask
    .filter((l) => l.action === 'Claimed')
    .reduce<string | null>((max, l) => (max === null || l.createdAt > max ? l.createdAt : max), null)
  if (latestClaimAt === null) return 'pending'

  const sinceLatestClaim = forTask.filter((l) => l.createdAt >= latestClaimAt)
  if (sinceLatestClaim.some((l) => l.action === 'TailoredContentSaved')) return 'saved'
  if (sinceLatestClaim.some((l) => l.action === 'RolledBack')) return 'rolled-back'
  return 'in-progress'
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

// Clusters adjacent same-applicationId tasks within ONE column's own task list into visual groups
// (Sprint 6, T6.4). Deliberately column-scoped, not board-wide: dnd-kit columns are separate drop
// containers, so a cross-column visual merge has no sound drag semantic. KanbanBoard.tsx already
// filters full ReviewReady pairs out of each column's list before it reaches here (they render via
// ApplicationReviewCard instead), so this only ever sees partial/non-ReviewReady siblings - never
// conflicting with that mechanism. A task with a null applicationId, or whose sibling isn't in this
// same column right now, ends up in its own 1-item group and renders exactly as before.
//
// Only the immediately preceding group may be extended (PR #49 review finding, Copilot): the task
// list is sorted by due date/priority, not grouped by application, so two siblings can legitimately
// be non-adjacent with an unrelated task between them. Matching against every earlier open group
// (not just the last one) would cluster those non-adjacent siblings anyway, reordering the
// rendered DOM relative to SortableContext's own items order (which follows the original task
// list) and breaking drag-position calculations. Groups only ever grow to 2: the last group stops
// being a match target as soon as a second task joins it, so a hypothetical third same-applicationId
// task in one column (never expected given the two-sibling domain model, but handled defensively
// rather than silently dropped or merged) starts its own new 1-item group instead of growing the
// pair to 3.
export function groupSiblingCards(tasks: TaskItem[]): TaskItem[][] {
  const groups: TaskItem[][] = []
  for (const task of tasks) {
    const lastGroup = groups[groups.length - 1]
    const canJoinLastGroup =
      task.applicationId !== null &&
      lastGroup?.length === 1 &&
      lastGroup[0].applicationId === task.applicationId
    if (canJoinLastGroup) {
      lastGroup.push(task)
    } else {
      groups.push([task])
    }
  }
  return groups
}
