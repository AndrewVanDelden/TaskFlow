import { describe, it, expect } from 'vitest'
import type { AgentLog, TaskItem, TaskStatus } from '../types'
import { resolveDropColumn, taskOutput } from './board'

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
