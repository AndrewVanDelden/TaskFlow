import { describe, it, expect, vi } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'

// Uses the shared manual mock at __mocks__/@microsoft/signalr.ts so the hook never opens a
// real connection. This isolates the seed-from-getAgentLogs behavior (the higher-value check).
vi.mock('@microsoft/signalr')

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
