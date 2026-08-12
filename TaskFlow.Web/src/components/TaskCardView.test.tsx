import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import { TaskCardView } from './TaskCardView'
import type { TaskItem } from '../types'

const task: TaskItem = {
  id: 1,
  title: 'Ship it',
  description: 'now',
  status: 'Review',
  priority: 'High',
  dueDate: null,
  createdAt: '',
  updatedAt: '',
  assignedToId: null,
  assignedToName: null,
  kind: 'Generic',
  applicationId: null,
  tailoredContent: null,
}

describe('TaskCardView', () => {
  // The DragOverlay renders this outside any SortableContext, so it must work with no drag context.
  it('renders standalone with the title, priority, and assignee fallback', () => {
    render(<TaskCardView task={task} />)
    expect(screen.getByText('Ship it')).toBeInTheDocument()
    expect(screen.getByText('High')).toBeInTheDocument()
    expect(screen.getByText('Unassigned')).toBeInTheDocument()
  })

  it('shows no review controls by default', () => {
    render(<TaskCardView task={task} />)
    expect(screen.queryByRole('button', { name: 'Approve' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Reject' })).toBeNull()
  })

  it('shows the executor output when provided', () => {
    render(<TaskCardView task={task} output={['Planned the work.', 'Crimson leaves drift down']} />)
    expect(screen.getByText('Planned the work.')).toBeInTheDocument()
    expect(screen.getByText('Crimson leaves drift down')).toBeInTheDocument()
  })

  it('approves, and gates reject on a typed reason', async () => {
    const onApprove = vi.fn()
    const onReject = vi.fn()
    render(<TaskCardView task={task} onApprove={onApprove} onReject={onReject} />)

    const reject = screen.getByRole('button', { name: 'Reject' })
    expect(reject).toBeDisabled() // empty reason -> greyed out and unclickable

    await userEvent.click(screen.getByRole('button', { name: 'Approve' }))
    expect(onApprove).toHaveBeenCalledOnce()

    await userEvent.type(screen.getByPlaceholderText(/reason/i), 'Needs work')
    expect(reject).toBeEnabled()
    await userEvent.click(reject)
    expect(onReject).toHaveBeenCalledWith('Needs work')
  })

  it('shows export download controls for a Done task that belongs to a job application', () => {
    const doneResumeTask: TaskItem = { ...task, status: 'Done', applicationId: 10, kind: 'ResumeTailoring' }
    render(<TaskCardView task={doneResumeTask} />)

    expect(screen.getByRole('button', { name: /download pdf/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /download markdown/i })).toBeInTheDocument()
  })

  it('shows no download controls for a Done generic task with no job application', () => {
    const doneGenericTask: TaskItem = { ...task, status: 'Done', applicationId: null, kind: 'Generic' }
    render(<TaskCardView task={doneGenericTask} />)

    expect(screen.queryByRole('button', { name: /download pdf/i })).toBeNull()
    expect(screen.queryByRole('button', { name: /download markdown/i })).toBeNull()
  })

  it('shows no download controls for a not-yet-Done job-application task', () => {
    const reviewResumeTask: TaskItem = { ...task, status: 'Review', applicationId: 10, kind: 'ResumeTailoring' }
    render(<TaskCardView task={reviewResumeTask} />)

    expect(screen.queryByRole('button', { name: /download pdf/i })).toBeNull()
    expect(screen.queryByRole('button', { name: /download markdown/i })).toBeNull()
  })
})
