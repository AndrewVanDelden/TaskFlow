import { describe, it, expect } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import { useBaseResumeReuse } from './useBaseResumeReuse'

describe('useBaseResumeReuse', () => {
  it('exposes the caller\'s most recently saved resume when the server has one', async () => {
    server.use(
      http.get('*/api/JobApplications/resume-context/latest', () =>
        HttpResponse.json({ content: 'Old resume', contentFormat: 'text', updatedAt: '2026-08-01T00:00:00Z' })),
    )

    const { result } = renderHook(() => useBaseResumeReuse())

    await waitFor(() => expect(result.current.available).toBe(true))
    expect(result.current.content).toBe('Old resume')
    expect(result.current.updatedAt).toBe('2026-08-01T00:00:00Z')
  })

  // The default handler (src/test/handlers.ts) already 404s for this route — no server.use override
  // needed, matching this project's established MSW convention for "nothing there yet" defaults.
  it('stays unavailable with no error exposed when the server has nothing saved (default 404)', async () => {
    const { result } = renderHook(() => useBaseResumeReuse())

    // No success to await, so wait on a settled render tick instead of a truthy condition.
    await waitFor(() => expect(result.current).toBeDefined())
    // Give the rejected promise's .catch() a tick to run before asserting the negative.
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(result.current.available).toBe(false)
    // The hook has no error field at all - confirmed by construction, not by asserting an absent
    // field on an object shape that could still exist.
    expect(Object.keys(result.current)).toEqual(['available', 'content', 'updatedAt'])
  })
})
