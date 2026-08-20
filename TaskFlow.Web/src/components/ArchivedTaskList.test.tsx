import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import { ArchivedTaskList } from './ArchivedTaskList'
import type { TaskItem } from '../types'
import { axe } from '../test/axe'

const task = (overrides: Partial<TaskItem> = {}): TaskItem => ({
  id: 1,
  title: 'Ship it',
  description: null,
  status: 'Done',
  priority: 'High',
  dueDate: null,
  createdAt: '',
  updatedAt: '',
  assignedToId: null,
  assignedToName: null,
  kind: 'Generic',
  applicationId: null,
  tailoredContent: null,
  archivedAt: '2026-08-14T00:00:00Z',
  ...overrides,
})

describe('ArchivedTaskList', () => {
  it('renders the title, company fallback, and archived date for each task', () => {
    render(<ArchivedTaskList tasks={[task()]} onRestore={vi.fn()} />)

    expect(screen.getByText('Ship it')).toBeInTheDocument()
    expect(screen.getByText('—', { exact: false })).toBeInTheDocument()
  })

  it('shows the company when present', () => {
    render(<ArchivedTaskList tasks={[task({ company: 'Acme Corp' })]} onRestore={vi.fn()} />)

    expect(screen.getByText(/Acme Corp/)).toBeInTheDocument()
  })

  it('shows a kind badge for a non-Generic task', () => {
    render(<ArchivedTaskList tasks={[task({ kind: 'ResumeTailoring' })]} onRestore={vi.fn()} />)

    expect(screen.getByText('Resume')).toBeInTheDocument()
  })

  it('shows no kind badge for a Generic task', () => {
    render(<ArchivedTaskList tasks={[task()]} onRestore={vi.fn()} />)

    expect(screen.queryByText('Resume')).toBeNull()
    expect(screen.queryByText('Cover letter')).toBeNull()
  })

  it('calls onRestore with the task id when Restore is clicked', async () => {
    const onRestore = vi.fn()
    render(<ArchivedTaskList tasks={[task({ id: 7 })]} onRestore={onRestore} />)

    await userEvent.click(screen.getByRole('button', { name: 'Restore' }))

    expect(onRestore).toHaveBeenCalledWith(7)
  })

  it('shows export download controls for a Done+Approved Epic-3 task', () => {
    const doneResumeTask = task({ applicationId: 10, kind: 'ResumeTailoring', applicationState: 'Approved' })
    render(<ArchivedTaskList tasks={[doneResumeTask]} onRestore={vi.fn()} />)

    expect(screen.getByRole('button', { name: /download pdf/i })).toBeInTheDocument()
  })

  it('shows no export download controls for a task whose application is not Approved', () => {
    const notApproved = task({ applicationId: 10, kind: 'ResumeTailoring', applicationState: 'ReviewReady' })
    render(<ArchivedTaskList tasks={[notApproved]} onRestore={vi.fn()} />)

    expect(screen.queryByRole('button', { name: /download pdf/i })).toBeNull()
  })

  it('shows the empty state when there are no archived tasks', () => {
    render(<ArchivedTaskList tasks={[]} onRestore={vi.fn()} />)

    expect(screen.getByText('Nothing archived yet.')).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    const { container } = render(<ArchivedTaskList tasks={[task()]} onRestore={vi.fn()} />)

    expect(await axe(container)).toHaveNoViolations()
  })

  // PR #61 review finding: the list-row divider used the input-field border-white/10 pattern
  // instead of the borderDivider token (same category as AgentFeedList's own row divider).
  it('uses the borderDivider token for the row divider, not the input-field border-white/10 pattern', () => {
    render(<ArchivedTaskList tasks={[task()]} onRestore={vi.fn()} />)

    const row = screen.getByText('Ship it').closest('li') as HTMLElement
    expect(getComputedStyle(row).borderColor).toBe('rgba(233, 233, 237, 0.16)')
    expect(row.className).not.toContain('border-white/10')
  })
})
