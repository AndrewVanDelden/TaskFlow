import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { TaskCardView } from './TaskCardView'
import type { TaskItem } from '../types'
import { mockPrefersReducedMotion } from '../test/reducedMotion'
import { axe } from '../test/axe'

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
  company: null,
}

describe('TaskCardView', () => {
  // TaskCardView now calls usePrefersReducedMotion() unconditionally (Rules of Hooks - it can't be
  // called only for InProgress cards), and jsdom has no real window.matchMedia. Every test that
  // renders the component needs it mocked, not just the ones asserting on the progress bar.
  beforeEach(() => {
    mockPrefersReducedMotion(false)
  })

  // The DragOverlay renders this outside any SortableContext, so it must work with no drag context.
  it('renders standalone with the title, priority, and company fallback', () => {
    render(<TaskCardView task={task} />)
    expect(screen.getByText('Ship it')).toBeInTheDocument()
    expect(screen.getByText('High')).toBeInTheDocument()
    expect(screen.getByText('—')).toBeInTheDocument()
  })

  it('shows the company when provided', () => {
    const taskWithCompany: TaskItem = { ...task, company: 'Acme Corp' }
    render(<TaskCardView task={taskWithCompany} />)
    expect(screen.getByText('Acme Corp')).toBeInTheDocument()
  })

  it('shows the in-progress tailoring status line', () => {
    const inProgressTask: TaskItem = { ...task, status: 'InProgress', kind: 'ResumeTailoring' }
    render(<TaskCardView task={inProgressTask} />)
    expect(screen.getByText('Tailoring Resume…')).toBeInTheDocument()
  })

  it('animates the in-progress bar when motion is not reduced', () => {
    mockPrefersReducedMotion(false)
    const inProgressTask: TaskItem = { ...task, status: 'InProgress', kind: 'ResumeTailoring' }
    render(<TaskCardView task={inProgressTask} />)
    expect(screen.getByTestId('progress-fill').className).toBe(
      'absolute inset-x-0 bottom-0 h-0.5 bg-gradient-to-r from-[#796cbf] to-[#d2cefd] animate-pulse',
    )
  })

  it('shows a static progress bar when motion is reduced', () => {
    mockPrefersReducedMotion(true)
    const inProgressTask: TaskItem = { ...task, status: 'InProgress', kind: 'ResumeTailoring' }
    render(<TaskCardView task={inProgressTask} />)
    expect(screen.getByTestId('progress-fill').className).toBe(
      'absolute inset-x-0 bottom-0 h-0.5 bg-gradient-to-r from-[#796cbf] to-[#d2cefd]',
    )
  })

  it('has no accessibility violations', async () => {
    const { container } = render(<TaskCardView task={task} />)
    expect(await axe(container)).toHaveNoViolations()
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

  it('shows export download controls for a Done task whose application is Approved', () => {
    const doneResumeTask: TaskItem = { ...task, status: 'Done', applicationId: 10, kind: 'ResumeTailoring', applicationState: 'Approved' }
    render(<TaskCardView task={doneResumeTask} />)

    expect(screen.getByRole('button', { name: /download pdf/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /download markdown/i })).toBeInTheDocument()
  })

  // Copilot review finding (PR #48): a lone Epic-3 sibling task can reach Done via the individual
  // per-task approve path while its own JobApplication is still ReviewReady/Building (only both
  // siblings approved together flips the application to Approved) - the export would be guaranteed
  // to 400 in that case, so the buttons must not render just because Status is Done.
  it('shows no download controls for a Done task whose application is not yet Approved', () => {
    const doneButNotApproved: TaskItem = { ...task, status: 'Done', applicationId: 10, kind: 'ResumeTailoring', applicationState: 'ReviewReady' }
    render(<TaskCardView task={doneButNotApproved} />)

    expect(screen.queryByRole('button', { name: /download pdf/i })).toBeNull()
    expect(screen.queryByRole('button', { name: /download markdown/i })).toBeNull()
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

  // Sprint 6, T6.4: every card shows its kind (unconditionally, not just grouped ones).
  it('shows a kind badge for a ResumeTailoring task', () => {
    const resumeTask: TaskItem = { ...task, kind: 'ResumeTailoring', applicationId: 10 }
    render(<TaskCardView task={resumeTask} />)

    expect(screen.getByText('Resume')).toBeInTheDocument()
  })

  it('shows a kind badge for a CoverLetterTailoring task', () => {
    const coverLetterTask: TaskItem = { ...task, kind: 'CoverLetterTailoring', applicationId: 10 }
    render(<TaskCardView task={coverLetterTask} />)

    expect(screen.getByText('Cover letter')).toBeInTheDocument()
  })

  it('shows no kind badge for a Generic task', () => {
    render(<TaskCardView task={task} />)

    expect(screen.queryByText('Resume')).toBeNull()
    expect(screen.queryByText('Cover letter')).toBeNull()
  })
})
