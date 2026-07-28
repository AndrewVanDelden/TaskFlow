import { describe, it, expect } from 'vitest'
import type { TaskItem, TaskStatus } from '../types'
import { resolveDropColumn } from './board'

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
