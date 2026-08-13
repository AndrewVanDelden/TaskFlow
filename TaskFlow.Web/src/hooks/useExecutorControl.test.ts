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

  it('stays at the unknown (null) state when the initial fetch fails', async () => {
    server.use(http.get('*/api/agents/executor', () => new HttpResponse(null, { status: 500 })))

    const { result } = renderHook(() => useExecutorControl())

    // No transition ever happens; give the failed fetch a tick to settle, then confirm it stayed null.
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.enabled).toBeNull()
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
