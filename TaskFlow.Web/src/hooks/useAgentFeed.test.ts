import { describe, it, expect, vi } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'

// Stub the SignalR client so mounting the hook never opens a real connection (which would
// fire an unhandled /hubs/agents negotiate request). This isolates the seed-from-getAgentLogs
// behavior, which is the higher-value assertion. Shape mirrors what useAgentFeed calls.
vi.mock('@microsoft/signalr', () => {
  class FakeHubConnection {
    state = 'Disconnected'
    on() {}
    onreconnected() {}
    onclose() {}
    start() {
      return Promise.resolve()
    }
    stop() {
      return Promise.resolve()
    }
  }
  class HubConnectionBuilder {
    withUrl() {
      return this
    }
    withAutomaticReconnect() {
      return this
    }
    build() {
      return new FakeHubConnection()
    }
  }
  return {
    HubConnectionBuilder,
    HubConnectionState: { Disconnected: 'Disconnected' },
  }
})

import { useAgentFeed } from './useAgentFeed'

describe('useAgentFeed', () => {
  it('seeds its logs from getAgentLogs', async () => {
    server.use(
      http.get('*/api/AgentLogs', () =>
        HttpResponse.json([
          {
            id: 1,
            taskId: null,
            agentName: 'TaskPrioritizer',
            action: 'PrioritiesUpdated',
            details: null,
            success: true,
            createdAt: '2026-07-26T12:00:00Z',
          },
        ]),
      ),
    )

    const { result } = renderHook(() => useAgentFeed())

    await waitFor(() => expect(result.current.logs).toHaveLength(1))
    expect(result.current.logs[0].action).toBe('PrioritiesUpdated')
  })
})
