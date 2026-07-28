import { render, screen } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import { KanbanBoard } from './KanbanBoard'

describe('KanbanBoard integration', () => {
  it('loads tasks from the API and renders them on the board', async () => {
    server.use(
      http.get('*/api/Tasks', () =>
        HttpResponse.json([
          {
            id: 1,
            title: 'Wire the dashboard',
            description: null,
            status: 'Todo',
            priority: 'High',
            dueDate: null,
            createdAt: '',
            updatedAt: '',
            assignedToId: null,
            assignedToName: null,
          },
        ]),
      ),
    )

    // KanbanBoard provides its own DndContext, so no wrapper is needed. With no AgentHubProvider
    // the shared connection is null, so the board just does its initial load. This exercises the
    // real flow: KanbanBoard -> useBoardTasks -> getTasks (api/tasks) -> MSW -> KanbanColumn -> TaskCard.
    render(<KanbanBoard />)

    expect(await screen.findByText('Wire the dashboard')).toBeInTheDocument()
  })
})
