import { render, screen } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import { DndContext } from '@dnd-kit/core'
import { KanbanColumn } from './KanbanColumn'
import type { TaskItem } from '../types'

const task: TaskItem = {
  id: 1, title: 'Ship it', description: null, status: 'Todo',
  priority: 'High', dueDate: null, createdAt: '', updatedAt: '',
  assignedToId: null, assignedToName: null,
  kind: 'Generic', applicationId: null, tailoredContent: null,
}

// KanbanColumn uses useDroppable and renders TaskCards (useSortable), so wrap in DndContext.
describe('KanbanColumn', () => {
  it('shows its label and renders the tasks it is given', () => {
    render(
      <DndContext>
        <KanbanColumn status="Todo" label="To Do" tasks={[task]} />
      </DndContext>,
    )
    expect(screen.getByText('To Do')).toBeInTheDocument()
    expect(screen.getByText('Ship it')).toBeInTheDocument()
  })

  it('shows the empty state when there are no tasks', () => {
    render(
      <DndContext>
        <KanbanColumn status="Todo" label="To Do" tasks={[]} />
      </DndContext>,
    )
    expect(screen.getByText('No tasks')).toBeInTheDocument()
  })
})
