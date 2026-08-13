import { describe, it, expect } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import { useExecutorControl } from './useExecutorControl'

// Epic 3 Pre-Merge Code Review, finding 4.4/6.4: ExecutorControl inlined its own fetch/toggle
// logic (unlike every sibling component, which delegates to a useXxx hook) and only its
// enable/happy-path was tested. Extracted to this hook so it can be tested directly; these tests
// cover the previously-untested disable path, the toggle's catch block, and the initial-fetch
// failure branch.
describe('useExecutorControl', () => {
  it('loads the initial enabled state on mount', async () => {
    server.use(http.get('*/api/agents/executor', () => HttpResponse.json({ enabled: true })))

    const { result } = renderHook(() => useExecutorControl())

    await waitFor(() => expect(result.current.enabled).toBe(true))
  })

  // Copilot's automated review (PR #50): the original version of this test waited on `busy`,
  // which the initial load never touches (only toggle() does) - so it was already `false` before
  // the request even started, and the test would still pass even if the fetch/effect were deleted
  // entirely. Gate on proof the request actually happened instead.
  it('stays at the unknown (null) state when the initial fetch fails', async () => {
    let requestReceived = false
    server.use(
      http.get('*/api/agents/executor', () => {
        requestReceived = true
        return new HttpResponse(null, { status: 500 })
      }),
    )

    const { result } = renderHook(() => useExecutorControl())

    // Fails (times out) if the initial fetch is ever removed, unlike asserting on `busy`.
    await waitFor(() => expect(requestReceived).toBe(true))
    // waitFor's real-timer polling gives the rejected request's .catch() time to run before the
    // next check, so this isn't trivially true the instant requestReceived flips.
    await waitFor(() => {
      expect(result.current.enabled).toBeNull()
      expect(result.current.busy).toBe(false)
    })
  })

  it('toggle() disables an enabled executor', async () => {
    server.use(
      http.get('*/api/agents/executor', () => HttpResponse.json({ enabled: true })),
      http.post('*/api/agents/executor/disable', () => HttpResponse.json({ enabled: false })),
    )
    const { result } = renderHook(() => useExecutorControl())
    await waitFor(() => expect(result.current.enabled).toBe(true))

    await act(() => result.current.toggle())

    expect(result.current.enabled).toBe(false)
    expect(result.current.busy).toBe(false)
  })

  it('toggle() keeps the current state when the API call fails', async () => {
    server.use(
      http.get('*/api/agents/executor', () => HttpResponse.json({ enabled: true })),
      http.post('*/api/agents/executor/disable', () => new HttpResponse(null, { status: 500 })),
    )
    const { result } = renderHook(() => useExecutorControl())
    await waitFor(() => expect(result.current.enabled).toBe(true))

    await act(() => result.current.toggle())

    expect(result.current.enabled).toBe(true) // unchanged
    expect(result.current.busy).toBe(false)
  })

  it('toggle() is a no-op while the initial state is still unknown', async () => {
    server.use(http.get('*/api/agents/executor', () => new Promise(() => {}))) // never resolves
    const { result } = renderHook(() => useExecutorControl())

    await act(() => result.current.toggle())

    expect(result.current.enabled).toBeNull()
    expect(result.current.busy).toBe(false)
  })
})
