import { render, screen } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import { DndContext } from '@dnd-kit/core'
import { TaskCard } from './TaskCard'
import type { TaskItem } from '../types'

const task: TaskItem = {
  id: 1, title: 'Ship it', description: 'now', status: 'Todo',
  priority: 'High', dueDate: null, createdAt: '', updatedAt: '',
  assignedToId: null, assignedToName: null,
}

// TaskCard uses useSortable, which throws outside a drag context, so wrap in DndContext.
describe('TaskCard', () => {
  it('shows the title and priority badge', () => {
    render(<DndContext><TaskCard task={task} /></DndContext>)
    expect(screen.getByText('Ship it')).toBeInTheDocument()
    expect(screen.getByText('High')).toBeInTheDocument()
  })

  it('shows Unassigned when there is no assignee', () => {
    render(<DndContext><TaskCard task={task} /></DndContext>)
    expect(screen.getByText('Unassigned')).toBeInTheDocument()
  })
})
