import { describe, it, expect, vi } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import type { ReactNode } from 'react'
import type { HubConnection } from '@microsoft/signalr'
import { server } from '../test/server'
import { AgentHubContext } from '../lib/AgentHubContext'
import { HubEvents } from '../lib/hubEvents'

// Uses the shared manual mock at __mocks__/@microsoft/signalr.ts so the hook never opens a
// real connection. This isolates the seed-from-getAgentLogs behavior (the higher-value check).
vi.mock('@microsoft/signalr')

import { useAgentFeed } from './useAgentFeed'

// A controllable fake connection injected via context, matching useBoardTasks.test.tsx's pattern -
// lets a test push AgentCycle/AgentAction events synchronously without a real connection.
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
    <AgentHubContext.Provider value={{ connection: connection as unknown as HubConnection, connected: true }}>
      {children}
    </AgentHubContext.Provider>
  )
}

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

  // User report (2026-08-24): the Task Prioritizer / Stale Task Detector status badges never
  // visibly flip to "Running" - "I think the agents run so fast that the idle never actually turns
  // to running." Confirmed: a 'started' AgentCycle event immediately followed by 'completed' (which
  // real cycles can do in a few ms) overwrote the same `cycles[agentName]` entry before the browser
  // ever painted the intermediate state. The fix holds a 'started' phase visible for a minimum
  // duration regardless of how fast the real cycle actually finished.
  it('keeps a cycle visible as started for a minimum duration even if it completes almost instantly', async () => {
    vi.useFakeTimers()
    const connection = makeFakeConnection()

    const { result } = renderHook(() => useAgentFeed(), { wrapper: makeWrapper(connection) })

    act(() => {
      connection.emit(HubEvents.AgentCycle, { agentName: 'TaskPrioritizer', phase: 'started', at: new Date().toISOString() })
      connection.emit(HubEvents.AgentCycle, { agentName: 'TaskPrioritizer', phase: 'completed', at: new Date().toISOString() })
    })

    expect(result.current.cycles.TaskPrioritizer.phase).toBe('started')

    await act(async () => {
      await vi.advanceTimersByTimeAsync(500)
    })

    expect(result.current.cycles.TaskPrioritizer.phase).toBe('completed')

    vi.useRealTimers()
  })

  // PR #71 review finding (Antigravity/Gemini, independently confirmed by a second manual review):
  // pendingTimeouts was a flat array, not keyed per agent, so a fast second cycle's immediate
  // 'started' apply could later be clobbered by the *first* cycle's still-pending delayed
  // 'completed' timeout firing on its own original schedule - the badge would show stale
  // 'completed' data while the agent was actually mid-cycle again.
  it('does not let an earlier cycles delayed completion clobber a later cycle already in progress', async () => {
    vi.useFakeTimers()
    const connection = makeFakeConnection()

    const { result } = renderHook(() => useAgentFeed(), { wrapper: makeWrapper(connection) })

    // Cycle 1: started then completed immediately - schedules a delayed apply ~500ms out.
    act(() => {
      connection.emit(HubEvents.AgentCycle, { agentName: 'TaskPrioritizer', phase: 'started', at: new Date().toISOString() })
      connection.emit(HubEvents.AgentCycle, { agentName: 'TaskPrioritizer', phase: 'completed', at: new Date().toISOString() })
    })

    // Before that delayed apply fires, cycle 2 starts.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(100)
    })
    act(() => {
      connection.emit(HubEvents.AgentCycle, { agentName: 'TaskPrioritizer', phase: 'started', at: new Date().toISOString() })
    })

    expect(result.current.cycles.TaskPrioritizer.phase).toBe('started')

    // Advance past the point where cycle 1's original delayed 'completed' would have fired.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(450)
    })

    expect(result.current.cycles.TaskPrioritizer.phase).toBe('started') // still cycle 2, not cycle 1's stale completion

    vi.useRealTimers()
  })
})
