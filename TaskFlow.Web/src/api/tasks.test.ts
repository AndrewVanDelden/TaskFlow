import { describe, it, expect } from 'vitest'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import { getTasks, archiveTask, unarchiveTask, archiveAllDone } from './tasks'
import type { TaskItem } from '../types'

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
  ...overrides,
})

describe('getTasks', () => {
  it('requests the default (non-archived) task list', async () => {
    let capturedUrl = ''
    server.use(
      http.get('*/api/Tasks', ({ request }) => {
        capturedUrl = request.url
        return HttpResponse.json([task()])
      }),
    )

    const result = await getTasks()

    expect(capturedUrl).toContain('archived=false')
    expect(result).toHaveLength(1)
  })

  it('requests the archived task list when archived=true is passed', async () => {
    let capturedUrl = ''
    server.use(
      http.get('*/api/Tasks', ({ request }) => {
        capturedUrl = request.url
        return HttpResponse.json([task({ archivedAt: '2026-08-14T00:00:00Z' })])
      }),
    )

    const result = await getTasks(true)

    expect(capturedUrl).toContain('archived=true')
    expect(result[0].archivedAt).toBe('2026-08-14T00:00:00Z')
  })
})

describe('archiveTask', () => {
  it('posts to the archive endpoint and returns the updated task', async () => {
    let capturedMethod = ''
    server.use(
      http.post('*/api/Tasks/1/archive', ({ request }) => {
        capturedMethod = request.method
        return HttpResponse.json(task({ archivedAt: '2026-08-14T00:00:00Z' }))
      }),
    )

    const result = await archiveTask(1)

    expect(capturedMethod).toBe('POST')
    expect(result.archivedAt).toBe('2026-08-14T00:00:00Z')
  })
})

describe('unarchiveTask', () => {
  it('posts to the unarchive endpoint and returns the updated task', async () => {
    let capturedMethod = ''
    server.use(
      http.post('*/api/Tasks/1/unarchive', ({ request }) => {
        capturedMethod = request.method
        return HttpResponse.json(task({ archivedAt: null }))
      }),
    )

    const result = await unarchiveTask(1)

    expect(capturedMethod).toBe('POST')
    expect(result.archivedAt).toBeNull()
  })
})

describe('archiveAllDone', () => {
  it('posts to the bulk archive-done endpoint and returns the archived count', async () => {
    let capturedMethod = ''
    server.use(
      http.post('*/api/Tasks/archive-done', ({ request }) => {
        capturedMethod = request.method
        return HttpResponse.json({ archivedCount: 3 })
      }),
    )

    const result = await archiveAllDone()

    expect(capturedMethod).toBe('POST')
    expect(result.archivedCount).toBe(3)
  })
})
