import { renderHook, act, waitFor } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import { http, HttpResponse } from 'msw'
import type { ReactNode } from 'react'
import type { HubConnection } from '@microsoft/signalr'
import { server } from '../test/server'
import { AgentHubContext } from '../lib/agentHub'
import { HubEvents } from '../lib/hubEvents'
import { useBoardTasks } from './useBoardTasks'

// A controllable fake connection injected via context, so the test can push a TaskMoved event
// without a real (or auto-mocked) SignalR connection.
function makeFakeConnection() {
  const handlers: Record<string, ((payload: unknown) => void)[]> = {}
  return {
    on: (e: string, cb: (payload: unknown) => void) => {
      handlers[e] = handlers[e] ?? []
      handlers[e].push(cb)
    },
    off: (e: string, cb: (payload: unknown) => void) => {
      handlers[e] = (handlers[e] ?? []).filter((h) => h !== cb)
    },
    emit: (e: string, payload: unknown) => (handlers[e] ?? []).forEach((h) => h(payload)),
  }
}

function makeWrapper(connection: ReturnType<typeof makeFakeConnection>) {
  return ({ children }: { children: ReactNode }) => (
    <AgentHubContext.Provider
      value={{ connection: connection as unknown as HubConnection, connected: true }}
    >
      {children}
    </AgentHubContext.Provider>
  )
}

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

describe('useBoardTasks', () => {
  it('patches only the card named in a TaskMoved event, leaving others untouched', async () => {
    server.use(
      http.get('*/api/Tasks', () =>
        HttpResponse.json([card(1, 'Card A', 'Todo'), card(2, 'Card B', 'Todo')]),
      ),
    )
    const connection = makeFakeConnection()

    const { result } = renderHook(() => useBoardTasks(), { wrapper: makeWrapper(connection) })

    await waitFor(() => expect(result.current.tasks).toHaveLength(2))

    act(() => connection.emit(HubEvents.TaskMoved, { id: 1, status: 'Review' }))

    expect(result.current.tasks.find((t) => t.id === 1)!.status).toBe('Review')
    expect(result.current.tasks.find((t) => t.id === 2)!.status).toBe('Todo') // untouched
  })
})
