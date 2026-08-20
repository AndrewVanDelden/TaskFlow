import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import { KanbanBoard } from './KanbanBoard'

const card = (id: number, title: string, status: string, kind = 'Generic', applicationId: number | null = null) => ({
  id,
  title,
  description: null,
  status,
  priority: 'High',
  dueDate: null,
  createdAt: '',
  updatedAt: '',
  assignedToId: null,
  assignedToName: null,
  kind,
  applicationId,
  tailoredContent: null,
})

describe('KanbanBoard integration', () => {
  // No AgentHubProvider: the shared connection is null, so the board just does its initial load.
  it('loads tasks from the API and renders them on the board', async () => {
    server.use(http.get('*/api/Tasks', () => HttpResponse.json([card(1, 'Wire the dashboard', 'Todo')])))

    render(<KanbanBoard />)

    expect(await screen.findByText('Wire the dashboard')).toBeInTheDocument()
  })

  // Epic 3 Pre-Merge Code Review, finding 6.4: the board-level error banner (KanbanBoard.tsx:49-53)
  // was never rendered by a test.
  it('shows an alert when the initial task load fails', async () => {
    server.use(http.get('*/api/Tasks', () => new HttpResponse('Server error', { status: 500 })))

    render(<KanbanBoard />)

    const alert = await screen.findByRole('alert')
    expect(alert).not.toHaveTextContent('')
  })

  it('shows Approve only on Review cards and approves to Done', async () => {
    server.use(
      http.get('*/api/Tasks', () =>
        HttpResponse.json([card(1, 'Ship the feature', 'Review'), card(2, 'Backlog item', 'Todo')]),
      ),
      http.post('*/api/Tasks/1/approve', () => HttpResponse.json(card(1, 'Ship the feature', 'Done'))),
    )

    render(<KanbanBoard />)

    // Only the Review card carries an Approve button. Exact name so dnd-kit's sortable wrapper
    // (role="button", whose name contains the card's text including "Approve") is not matched.
    const approveButtons = await screen.findAllByRole('button', { name: 'Approve' })
    expect(approveButtons).toHaveLength(1)

    await userEvent.click(approveButtons[0])

    // Optimistic move to Done leaves no Approve button (the card left Review; Todo never had one).
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Approve' })).toBeNull())
  })

  it('renders a ready pair (both siblings Review) as one combined review card, not two task cards', async () => {
    server.use(
      http.get('*/api/Tasks', () =>
        HttpResponse.json([
          card(1, 'Tailor resume', 'Review', 'ResumeTailoring', 10),
          card(2, 'Tailor cover letter', 'Review', 'CoverLetterTailoring', 10),
        ]),
      ),
      http.get('*/api/JobApplications/10/resume-context', () => HttpResponse.json('Base resume text')),
    )

    render(<KanbanBoard />)

    expect(await screen.findByText('Application review')).toBeInTheDocument()
    // The paired tasks must not ALSO render as individual TaskCards.
    expect(screen.queryByText('Tailor resume')).toBeNull()
    expect(screen.queryByText('Tailor cover letter')).toBeNull()
    // Only one combined Approve/Reject pair for the application, not two individual ones.
    expect(await screen.findAllByRole('button', { name: 'Approve' })).toHaveLength(1)
  })

  it('renders a single un-paired Review task as a normal TaskCard with its own approve/reject', async () => {
    server.use(
      http.get('*/api/Tasks', () =>
        HttpResponse.json([card(1, 'Solo generic review', 'Review', 'Generic', null)]),
      ),
    )

    render(<KanbanBoard />)

    expect(await screen.findByText('Solo generic review')).toBeInTheDocument()
    expect(await screen.findAllByRole('button', { name: 'Approve' })).toHaveLength(1)
  })

  it('leaves Todo/InProgress/Done columns unaffected by pairing', async () => {
    server.use(
      http.get('*/api/Tasks', () =>
        HttpResponse.json([
          card(1, 'Todo task', 'Todo'),
          card(2, 'In progress task', 'InProgress'),
          card(3, 'Done task', 'Done'),
        ]),
      ),
    )

    render(<KanbanBoard />)

    expect(await screen.findByText('Todo task')).toBeInTheDocument()
    expect(screen.getByText('In progress task')).toBeInTheDocument()
    expect(screen.getByText('Done task')).toBeInTheDocument()
    // None of these columns show Approve/Reject controls.
    expect(screen.queryByRole('button', { name: 'Approve' })).toBeNull()
  })

  it('shows Archive only on Done cards and archiving removes the card from the board', async () => {
    server.use(
      http.get('*/api/Tasks', () =>
        HttpResponse.json([card(1, 'Ship the feature', 'Done'), card(2, 'Backlog item', 'Todo')]),
      ),
      http.post('*/api/Tasks/1/archive', () => HttpResponse.json({ ...card(1, 'Ship the feature', 'Done'), archivedAt: '2026-08-14T00:00:00Z' })),
    )

    render(<KanbanBoard />)

    const archiveButtons = await screen.findAllByRole('button', { name: 'Archive' })
    expect(archiveButtons).toHaveLength(1)

    await userEvent.click(archiveButtons[0])

    await waitFor(() => expect(screen.queryByText('Ship the feature')).toBeNull())
  })

  it('clears every Done card via the Done column\'s Clear Done button after confirming', async () => {
    const user = userEvent.setup()
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    server.use(
      http.get('*/api/Tasks', () =>
        HttpResponse.json([
          card(1, 'Done task A', 'Done'),
          card(2, 'Done task B', 'Done'),
          card(3, 'Todo task', 'Todo'),
        ]),
      ),
      http.post('*/api/Tasks/archive-done', () => HttpResponse.json({ archivedCount: 2 })),
    )

    render(<KanbanBoard />)
    await screen.findByText('Done task A')

    // Only the Done column ever renders this button, so no need to scope the query to it.
    await user.click(screen.getByRole('button', { name: 'Clear Done' }))

    await waitFor(() => expect(screen.queryByText('Done task A')).toBeNull())
    expect(screen.queryByText('Done task B')).toBeNull()
    expect(screen.getByText('Todo task')).toBeInTheDocument()

    vi.restoreAllMocks()
  })
})
