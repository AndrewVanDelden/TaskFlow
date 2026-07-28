import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect } from 'vitest'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import { KanbanBoard } from './KanbanBoard'

const card = (id: number, title: string, status: string) => ({
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
})

describe('KanbanBoard integration', () => {
  // No AgentHubProvider: the shared connection is null, so the board just does its initial load.
  it('loads tasks from the API and renders them on the board', async () => {
    server.use(http.get('*/api/Tasks', () => HttpResponse.json([card(1, 'Wire the dashboard', 'Todo')])))

    render(<KanbanBoard />)

    expect(await screen.findByText('Wire the dashboard')).toBeInTheDocument()
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
})
