import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect } from 'vitest'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import { Archive } from './Archive'
import type { TaskItem } from '../types'
import { axe } from '../test/axe'

const archivedTask: TaskItem = {
  id: 1,
  title: 'Old task',
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
}

describe('Archive', () => {
  it('renders the Archive heading', async () => {
    server.use(http.get('*/api/Tasks', () => HttpResponse.json([])))

    render(<Archive />)

    expect(screen.getByRole('heading', { name: 'Archive' })).toBeInTheDocument()
  })

  it('fetches and renders the archived task list', async () => {
    server.use(http.get('*/api/Tasks', () => HttpResponse.json([archivedTask])))

    render(<Archive />)

    expect(await screen.findByText('Old task')).toBeInTheDocument()
  })

  it('removes a task from the list when Restore succeeds', async () => {
    server.use(
      http.get('*/api/Tasks', () => HttpResponse.json([archivedTask])),
      http.post('*/api/Tasks/1/unarchive', () => HttpResponse.json({ ...archivedTask, archivedAt: null })),
    )
    render(<Archive />)
    await screen.findByText('Old task')

    await userEvent.click(screen.getByRole('button', { name: 'Restore' }))

    expect(await screen.findByText('Nothing archived yet.')).toBeInTheDocument()
  })

  it('shows the empty state when nothing is archived', async () => {
    server.use(http.get('*/api/Tasks', () => HttpResponse.json([])))

    render(<Archive />)

    expect(await screen.findByText('Nothing archived yet.')).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    server.use(http.get('*/api/Tasks', () => HttpResponse.json([archivedTask])))

    const { container } = render(<Archive />)
    await screen.findByText('Old task')

    expect(await axe(container)).toHaveNoViolations()
  })
})
