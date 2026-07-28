import { render, screen } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import { TaskCardView } from './TaskCardView'
import type { TaskItem } from '../types'

const task: TaskItem = {
  id: 1,
  title: 'Ship it',
  description: 'now',
  status: 'Todo',
  priority: 'High',
  dueDate: null,
  createdAt: '',
  updatedAt: '',
  assignedToId: null,
  assignedToName: null,
}

describe('TaskCardView', () => {
  // The DragOverlay renders this outside any SortableContext, so it must work with no drag context.
  it('renders standalone with the title, priority, and assignee fallback', () => {
    render(<TaskCardView task={task} />)
    expect(screen.getByText('Ship it')).toBeInTheDocument()
    expect(screen.getByText('High')).toBeInTheDocument()
    expect(screen.getByText('Unassigned')).toBeInTheDocument()
  })
})
