import { describe, it, expect } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import { useArchivedTasks } from './useArchivedTasks'
import type { TaskItem } from '../types'

const archivedTask = (id: number, title: string): TaskItem => ({
  id,
  title,
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
})

describe('useArchivedTasks', () => {
  it('fetches the archived task list on mount', async () => {
    let capturedUrl = ''
    server.use(
      http.get('*/api/Tasks', ({ request }) => {
        capturedUrl = request.url
        return HttpResponse.json([archivedTask(1, 'Old task')])
      }),
    )

    const { result } = renderHook(() => useArchivedTasks())

    await waitFor(() => expect(result.current.tasks).toHaveLength(1))
    expect(capturedUrl).toContain('archived=true')
    expect(result.current.tasks[0].title).toBe('Old task')
  })

  it('sets an error when the initial load fails', async () => {
    server.use(http.get('*/api/Tasks', () => new HttpResponse('Server error', { status: 500 })))

    const { result } = renderHook(() => useArchivedTasks())

    await waitFor(() => expect(result.current.error).not.toBeNull())
  })

  it('restoring a task removes it from the archived list on success', async () => {
    server.use(
      http.get('*/api/Tasks', () => HttpResponse.json([archivedTask(1, 'Old task')])),
      http.post('*/api/Tasks/1/unarchive', () => HttpResponse.json({ ...archivedTask(1, 'Old task'), archivedAt: null })),
    )

    const { result } = renderHook(() => useArchivedTasks())
    await waitFor(() => expect(result.current.tasks).toHaveLength(1))

    await act(async () => {
      await result.current.restore(1)
    })

    expect(result.current.tasks).toHaveLength(0)
  })

  it('rolls back and surfaces an error when restoring fails', async () => {
    server.use(
      http.get('*/api/Tasks', () => HttpResponse.json([archivedTask(1, 'Old task')])),
      http.post('*/api/Tasks/1/unarchive', () => new HttpResponse('Not allowed', { status: 400 })),
    )

    const { result } = renderHook(() => useArchivedTasks())
    await waitFor(() => expect(result.current.tasks).toHaveLength(1))

    await act(async () => {
      await result.current.restore(1)
    })

    expect(result.current.tasks).toHaveLength(1)
    expect(result.current.error).not.toBeNull()
  })
})
