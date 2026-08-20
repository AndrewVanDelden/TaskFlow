import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import { DndContext } from '@dnd-kit/core'
import { TaskCard } from './TaskCard'
import type { TaskItem } from '../types'

const task: TaskItem = {
  id: 1, title: 'Ship it', description: 'now', status: 'Todo',
  priority: 'High', dueDate: null, createdAt: '', updatedAt: '',
  assignedToId: null, assignedToName: null,
  kind: 'Generic', applicationId: null, tailoredContent: null,
}

// TaskCard uses useSortable, which throws outside a drag context, so wrap in DndContext.
describe('TaskCard', () => {
  it('shows the title and priority badge', () => {
    render(<DndContext><TaskCard task={task} /></DndContext>)
    expect(screen.getByText('Ship it')).toBeInTheDocument()
    expect(screen.getByText('High')).toBeInTheDocument()
  })

  it('shows the em-dash placeholder when there is no company', () => {
    render(<DndContext><TaskCard task={task} /></DndContext>)
    expect(screen.getByText('—')).toBeInTheDocument()
  })

  it('threads onArchive through to the Archive button for a Done task', async () => {
    const onArchive = vi.fn()
    const doneTask: TaskItem = { ...task, status: 'Done' }
    render(<DndContext><TaskCard task={doneTask} onArchive={onArchive} /></DndContext>)

    await userEvent.click(screen.getByRole('button', { name: 'Archive' }))

    expect(onArchive).toHaveBeenCalledOnce()
  })
})
