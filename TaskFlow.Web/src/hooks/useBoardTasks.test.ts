import { describe, it, expect } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import { useBoardTasks } from './useBoardTasks'
import type { TaskItem } from '../types'

const card = (id: number, title: string, status: string): TaskItem => ({
  id,
  title,
  description: null,
  status: status as TaskItem['status'],
  priority: 'High',
  dueDate: null,
  createdAt: '',
  updatedAt: '',
  assignedToId: null,
  assignedToName: null,
  kind: 'Generic',
  applicationId: null,
  tailoredContent: null,
})

// No AgentHubProvider: useAgentHub() falls back to its default context ({ connection: null }), so
// the hook just does its initial load, same convention as KanbanBoard.test.tsx's own hook tests.
describe('useBoardTasks archiving', () => {
  it('archiving one card removes it from the visible board', async () => {
    server.use(
      http.get('*/api/Tasks', () => HttpResponse.json([card(1, 'Done task', 'Done'), card(2, 'Todo task', 'Todo')])),
      http.post('*/api/Tasks/1/archive', () => HttpResponse.json({ ...card(1, 'Done task', 'Done'), archivedAt: '2026-08-14T00:00:00Z' })),
    )

    const { result } = renderHook(() => useBoardTasks())
    await waitFor(() => expect(result.current.tasks).toHaveLength(2))

    await act(async () => {
      await result.current.archive(1)
    })

    expect(result.current.tasks.map((t) => t.id)).toEqual([2])
  })

  it('rolls back and surfaces an error when archiving fails', async () => {
    server.use(
      http.get('*/api/Tasks', () => HttpResponse.json([card(1, 'Done task', 'Done')])),
      http.post('*/api/Tasks/1/archive', () => new HttpResponse('Not allowed', { status: 400 })),
    )

    const { result } = renderHook(() => useBoardTasks())
    await waitFor(() => expect(result.current.tasks).toHaveLength(1))

    await act(async () => {
      await result.current.archive(1)
    })

    expect(result.current.tasks.map((t) => t.id)).toEqual([1])
    expect(result.current.error).not.toBeNull()
  })

  it('clicking archiveDone archives everything currently Done and they disappear from the board', async () => {
    server.use(
      http.get('*/api/Tasks', () =>
        HttpResponse.json([card(1, 'Done task A', 'Done'), card(2, 'Done task B', 'Done'), card(3, 'Todo task', 'Todo')])),
      http.post('*/api/Tasks/archive-done', () => HttpResponse.json({ archivedCount: 2 })),
    )

    const { result } = renderHook(() => useBoardTasks())
    await waitFor(() => expect(result.current.tasks).toHaveLength(3))

    await act(async () => {
      await result.current.archiveDone()
    })

    expect(result.current.tasks.map((t) => t.id)).toEqual([3])
  })

  it('rolls back and surfaces an error when the bulk archive fails', async () => {
    server.use(
      http.get('*/api/Tasks', () => HttpResponse.json([card(1, 'Done task', 'Done'), card(2, 'Todo task', 'Todo')])),
      http.post('*/api/Tasks/archive-done', () => new HttpResponse('Server error', { status: 500 })),
    )

    const { result } = renderHook(() => useBoardTasks())
    await waitFor(() => expect(result.current.tasks).toHaveLength(2))

    await act(async () => {
      await result.current.archiveDone()
    })

    expect(result.current.tasks.map((t) => t.id)).toEqual([1, 2])
    expect(result.current.error).not.toBeNull()
  })
})
