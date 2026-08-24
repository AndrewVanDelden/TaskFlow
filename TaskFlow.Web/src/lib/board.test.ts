import { describe, it, expect } from 'vitest'
import type { AgentLog, TaskItem, TaskKind, TaskStatus } from '../types'
import {
  resolveDropColumn,
  taskOutput,
  taskStage,
  reviewReadyPairs,
  groupSiblingCards,
  canDownloadExport,
  displayTitle,
} from './board'

const task = (id: number, status: TaskStatus): TaskItem => ({
  id,
  title: `T${id}`,
  description: null,
  status,
  priority: 'Low',
  dueDate: null,
  createdAt: '',
  updatedAt: '',
  assignedToId: null,
  assignedToName: null,
  kind: 'Generic',
  applicationId: null,
  tailoredContent: null,
})

describe('resolveDropColumn', () => {
  const tasks = [task(1, 'Todo'), task(2, 'Review')]

  it('uses the column status when dropped on empty column space', () => {
    expect(resolveDropColumn('InProgress', tasks)).toBe('InProgress')
  })

  it('resolves to the target card\'s column when dropped onto a card', () => {
    // Dropping onto card 2 (which sits in Review) must move the dragged card to Review,
    // not set its status to the number 2.
    expect(resolveDropColumn(2, tasks)).toBe('Review')
  })

  it('returns null for an unknown drop target', () => {
    expect(resolveDropColumn(999, tasks)).toBeNull()
  })
})

const log = (taskId: number, action: string, details: string, createdAt: string): AgentLog => ({
  id: taskId * 100 + createdAt.length,
  taskId,
  agentName: 'GenericExecutor',
  action,
  details,
  success: true,
  createdAt,
})

describe('taskOutput', () => {
  it('shows only the latest cycle, oldest first, ignoring other tasks and earlier runs', () => {
    const logs = [
      // Latest cycle for task 1.
      log(1, 'ReviewRequested', 'Here is the haiku.', '2026-07-28T10:12:00Z'),
      log(1, 'ProgressRecorded', 'Planning the haiku.', '2026-07-28T10:11:00Z'),
      log(1, 'Claimed', 'claimed again', '2026-07-28T10:10:00Z'),
      // An earlier run / reused-id history for task 1 — must be excluded.
      log(1, 'ReviewRequested', 'Old CI/CD output.', '2026-07-28T09:02:00Z'),
      log(1, 'Claimed', 'claimed long ago', '2026-07-28T09:00:00Z'),
      // A different task — must be excluded.
      log(2, 'ReviewRequested', 'A different task.', '2026-07-28T10:13:00Z'),
    ]

    expect(taskOutput(logs, 1)).toEqual(['Planning the haiku.', 'Here is the haiku.'])
  })

  it('returns nothing when there is no claim for the task in the log window', () => {
    const logs = [log(1, 'ReviewRequested', 'stale, no claim', '2026-07-28T09:00:00Z')]
    expect(taskOutput(logs, 1)).toEqual([])
  })
})

describe('taskStage', () => {
  it('returns pending when the task has never been claimed', () => {
    const logs = [log(2, 'Claimed', 'claimed', '2026-07-28T09:00:00Z')]
    expect(taskStage([], 1)).toBe('pending')
    expect(taskStage(logs, 1)).toBe('pending')
  })

  it('returns in-progress after Claimed with no further action yet', () => {
    const logs = [log(1, 'Claimed', 'claimed', '2026-07-28T10:00:00Z')]
    expect(taskStage(logs, 1)).toBe('in-progress')
  })

  it('returns saved after TailoredContentSaved', () => {
    const logs = [
      log(1, 'Claimed', 'claimed', '2026-07-28T10:00:00Z'),
      log(1, 'TailoredContentSaved', 'saved content', '2026-07-28T10:05:00Z'),
    ]
    expect(taskStage(logs, 1)).toBe('saved')
  })

  it('returns rolled-back after RolledBack', () => {
    const logs = [
      log(1, 'Claimed', 'claimed', '2026-07-28T10:00:00Z'),
      log(1, 'RolledBack', 'rolled back', '2026-07-28T10:05:00Z'),
    ]
    expect(taskStage(logs, 1)).toBe('rolled-back')
  })

  it('ignores a stale prior cycle before a rollback-and-reclaim', () => {
    const logs = [
      // Stale prior cycle: claimed, then rolled back.
      log(1, 'Claimed', 'claimed first', '2026-07-28T09:00:00Z'),
      log(1, 'RolledBack', 'rolled back', '2026-07-28T09:05:00Z'),
      // Current cycle: reclaimed, nothing after it yet.
      log(1, 'Claimed', 'claimed again', '2026-07-28T10:00:00Z'),
    ]
    expect(taskStage(logs, 1)).toBe('in-progress')
  })

  it('ignores log entries for a different task id', () => {
    const logs = [
      log(1, 'Claimed', 'claimed', '2026-07-28T10:00:00Z'),
      log(1, 'TailoredContentSaved', 'saved content', '2026-07-28T10:05:00Z'),
      log(2, 'Claimed', 'claimed', '2026-07-28T10:10:00Z'),
    ]
    expect(taskStage(logs, 2)).toBe('in-progress')
  })
})

// Builds an Epic 3 task (resume/cover-letter tailoring) with sensible generic-task defaults for
// the unrelated TaskItem fields, so each test only has to spell out what it's actually varying.
const epicTask = (
  id: number,
  applicationId: number | null,
  kind: TaskKind,
  status: TaskStatus,
): TaskItem => ({
  id,
  title: `T${id}`,
  description: null,
  status,
  priority: 'Low',
  dueDate: null,
  createdAt: '',
  updatedAt: '',
  assignedToId: null,
  assignedToName: null,
  kind,
  applicationId,
  tailoredContent: null,
})

describe('reviewReadyPairs', () => {
  it('pairs a resume and cover-letter task for the same application, both Review', () => {
    const resumeTask = epicTask(1, 10, 'ResumeTailoring', 'Review')
    const coverLetterTask = epicTask(2, 10, 'CoverLetterTailoring', 'Review')

    expect(reviewReadyPairs([resumeTask, coverLetterTask])).toEqual([
      { applicationId: 10, resumeTask, coverLetterTask },
    ])
  })

  it('does not pair when only one sibling is Review', () => {
    const resumeTask = epicTask(1, 10, 'ResumeTailoring', 'Review')
    const coverLetterTask = epicTask(2, 10, 'CoverLetterTailoring', 'InProgress')

    expect(reviewReadyPairs([resumeTask, coverLetterTask])).toEqual([])
  })

  it('does not pair two Review tasks of the same kind for one application, and does not crash', () => {
    const first = epicTask(1, 10, 'ResumeTailoring', 'Review')
    const second = epicTask(2, 10, 'ResumeTailoring', 'Review')

    expect(reviewReadyPairs([first, second])).toEqual([])
  })

  it('ignores generic tasks with a null applicationId', () => {
    const generic = epicTask(1, null, 'Generic', 'Review')

    expect(reviewReadyPairs([generic])).toEqual([])
  })

  it('returns pairs for multiple different applications independently', () => {
    const resumeA = epicTask(1, 10, 'ResumeTailoring', 'Review')
    const coverA = epicTask(2, 10, 'CoverLetterTailoring', 'Review')
    const resumeB = epicTask(3, 20, 'ResumeTailoring', 'Review')
    const coverB = epicTask(4, 20, 'CoverLetterTailoring', 'Review')

    const result = reviewReadyPairs([resumeA, coverA, resumeB, coverB])

    expect(result).toHaveLength(2)
    expect(result).toContainEqual({ applicationId: 10, resumeTask: resumeA, coverLetterTask: coverA })
    expect(result).toContainEqual({ applicationId: 20, resumeTask: resumeB, coverLetterTask: coverB })
  })

  it('does not pair when a sibling has already left Review (e.g. Done)', () => {
    const resumeTask = epicTask(1, 10, 'ResumeTailoring', 'Done')
    const coverLetterTask = epicTask(2, 10, 'CoverLetterTailoring', 'Review')

    expect(reviewReadyPairs([resumeTask, coverLetterTask])).toEqual([])
  })
})

describe('groupSiblingCards', () => {
  it('groups two adjacent same-applicationId tasks together', () => {
    const resumeTask = epicTask(1, 10, 'ResumeTailoring', 'Todo')
    const coverLetterTask = epicTask(2, 10, 'CoverLetterTailoring', 'InProgress')

    expect(groupSiblingCards([resumeTask, coverLetterTask])).toEqual([[resumeTask, coverLetterTask]])
  })

  it('does not group tasks from different applications', () => {
    const taskA = epicTask(1, 10, 'ResumeTailoring', 'Todo')
    const taskB = epicTask(2, 20, 'ResumeTailoring', 'Todo')

    expect(groupSiblingCards([taskA, taskB])).toEqual([[taskA], [taskB]])
  })

  it('does not group a Generic task with anything (null applicationId)', () => {
    const generic = epicTask(1, null, 'Generic', 'Todo')
    const epic = epicTask(2, 10, 'ResumeTailoring', 'Todo')

    expect(groupSiblingCards([generic, epic])).toEqual([[generic], [epic]])
  })

  it('handles a lone sibling whose pair is in a different column (not present in this list) as its own 1-item group', () => {
    const lone = epicTask(1, 10, 'ResumeTailoring', 'Todo')

    expect(groupSiblingCards([lone])).toEqual([[lone]])
  })

  it('preserves the original task order across groups', () => {
    const unrelatedA = epicTask(1, null, 'Generic', 'Todo')
    const resumeTask = epicTask(2, 10, 'ResumeTailoring', 'Todo')
    const coverLetterTask = epicTask(3, 10, 'CoverLetterTailoring', 'Todo')
    const unrelatedB = epicTask(4, null, 'Generic', 'Todo')

    expect(groupSiblingCards([unrelatedA, resumeTask, coverLetterTask, unrelatedB])).toEqual([
      [unrelatedA],
      [resumeTask, coverLetterTask],
      [unrelatedB],
    ])
  })

  // PR #49 review finding (Copilot, confirmed by hand-tracing the algorithm): the task list is
  // sorted by due date/priority (TaskRepository.GetAllAsync), not grouped by application, so two
  // siblings landing non-adjacent - with an unrelated task sorted between them - is an ordinary,
  // reachable case, not a contrived one. Grouping them anyway would reorder the rendered DOM
  // relative to SortableContext's own items order (which follows the original task list), breaking
  // drag-position calculations. Only truly adjacent same-applicationId tasks may be grouped.
  it('does not group two same-applicationId tasks that are not adjacent', () => {
    const resumeTask = epicTask(1, 10, 'ResumeTailoring', 'Todo')
    const unrelated = epicTask(2, null, 'Generic', 'Todo')
    const coverLetterTask = epicTask(3, 10, 'CoverLetterTailoring', 'Todo')

    expect(groupSiblingCards([resumeTask, unrelated, coverLetterTask])).toEqual([
      [resumeTask],
      [unrelated],
      [coverLetterTask],
    ])
  })

  // Epic 3 Pre-Merge Code Review, finding 6.4: the function's own comment calls this case out
  // ("a hypothetical third same-applicationId task... starts its own new 1-item group instead of
  // growing the pair to 3") but no test proved it. Never reachable given the two-sibling domain
  // model, but handled defensively rather than silently dropped or merged.
  it('starts a new group for a third same-applicationId task instead of growing the pair to 3', () => {
    const first = epicTask(1, 10, 'ResumeTailoring', 'Todo')
    const second = epicTask(2, 10, 'CoverLetterTailoring', 'Todo')
    const third = epicTask(3, 10, 'ResumeTailoring', 'Todo')

    expect(groupSiblingCards([first, second, third])).toEqual([[first, second], [third]])
  })
})

// PR #61 review finding: `task.status === 'Done' && task.applicationId !== null &&
// task.applicationState === 'Approved'` (the export-download gate) was duplicated verbatim in
// TaskCardView.tsx and ArchivedTaskList.tsx. Extracted here as the single source of truth for both.
describe('canDownloadExport', () => {
  it('is true for a Done task with a non-null applicationId whose application is Approved', () => {
    const doneApproved = epicTask(1, 10, 'ResumeTailoring', 'Done')
    doneApproved.applicationState = 'Approved'

    expect(canDownloadExport(doneApproved)).toBe(true)
  })

  it('is false when the task is not Done', () => {
    const reviewApproved = epicTask(1, 10, 'ResumeTailoring', 'Review')
    reviewApproved.applicationState = 'Approved'

    expect(canDownloadExport(reviewApproved)).toBe(false)
  })

  it('is false when applicationId is null', () => {
    const doneNoApplication = epicTask(1, null, 'Generic', 'Done')
    doneNoApplication.applicationState = 'Approved'

    expect(canDownloadExport(doneNoApplication)).toBe(false)
  })

  it('is false when the application state is not Approved', () => {
    const doneNotApproved = epicTask(1, 10, 'ResumeTailoring', 'Done')
    doneNotApproved.applicationState = 'ReviewReady'

    expect(canDownloadExport(doneNotApproved)).toBe(false)
  })
})

// User report (2026-08-24): a bare job-posting title on the board gives no indication which
// company an application is for once several are in flight at once.
describe('displayTitle', () => {
  it('appends the company when the task has one', () => {
    const withCompany: TaskItem = { ...task(1, 'Todo'), title: 'Senior Software Engineer', company: 'Acme Corp' }

    expect(displayTitle(withCompany)).toBe('Senior Software Engineer — Acme Corp')
  })

  it('falls back to the bare title when there is no company', () => {
    const noCompany: TaskItem = { ...task(1, 'Todo'), title: 'Senior Software Engineer', company: null }

    expect(displayTitle(noCompany)).toBe('Senior Software Engineer')
  })
})
