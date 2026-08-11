import { describe, it, expect } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import { useApplicationReview } from './useApplicationReview'

describe('useApplicationReview', () => {
  it('loads the base resume on mount', async () => {
    server.use(
      http.get('*/api/JobApplications/10/resume-context', () => HttpResponse.json('My base resume')),
    )

    const { result } = renderHook(() => useApplicationReview(10))

    await waitFor(() => expect(result.current.baseResume).toBe('My base resume'))
    expect(result.current.baseResumeLoading).toBe(false)
    expect(result.current.baseResumeError).toBeNull()
  })

  it('sets an error and does not crash when the base resume fetch fails', async () => {
    server.use(
      http.get('*/api/JobApplications/10/resume-context', () => new HttpResponse(null, { status: 404 })),
    )

    const { result } = renderHook(() => useApplicationReview(10))

    await waitFor(() => expect(result.current.baseResumeError).not.toBeNull())
    expect(result.current.baseResume).toBeNull()
    expect(result.current.baseResumeLoading).toBe(false)
  })

  it('approves successfully', async () => {
    server.use(
      http.get('*/api/JobApplications/10/resume-context', () => HttpResponse.json('base')),
      http.post('*/api/JobApplications/10/approve', () =>
        HttpResponse.json({
          id: 10, state: 'Approved', ingestionSessionId: '', ownerId: 1, createdAt: '', tasks: [],
        })),
    )

    const { result } = renderHook(() => useApplicationReview(10))
    await waitFor(() => expect(result.current.baseResumeLoading).toBe(false))

    await act(async () => {
      await result.current.approve()
    })

    expect(result.current.actionError).toBeNull()
    expect(result.current.actionLoading).toBe(false)
  })

  it('sets an action error when approve fails', async () => {
    server.use(
      http.get('*/api/JobApplications/10/resume-context', () => HttpResponse.json('base')),
      http.post('*/api/JobApplications/10/approve', () => new HttpResponse(null, { status: 409 })),
    )

    const { result } = renderHook(() => useApplicationReview(10))
    await waitFor(() => expect(result.current.baseResumeLoading).toBe(false))

    await act(async () => {
      await result.current.approve()
    })

    expect(result.current.actionError).not.toBeNull()
    expect(result.current.actionLoading).toBe(false)
  })

  it('rejects successfully with a reason', async () => {
    server.use(
      http.get('*/api/JobApplications/10/resume-context', () => HttpResponse.json('base')),
      http.post('*/api/JobApplications/10/reject', () =>
        HttpResponse.json({
          id: 10, state: 'Building', ingestionSessionId: '', ownerId: 1, createdAt: '', tasks: [],
        })),
    )

    const { result } = renderHook(() => useApplicationReview(10))
    await waitFor(() => expect(result.current.baseResumeLoading).toBe(false))

    await act(async () => {
      await result.current.reject('Needs more detail')
    })

    expect(result.current.actionError).toBeNull()
    expect(result.current.actionLoading).toBe(false)
  })

  // Copilot's automated review (PR #45): the effect started a new fetch on applicationId change
  // but never cleared the previous application's baseResume, so a caller that reuses this hook
  // across different ids (not the case for ApplicationReviewCard today, which is keyed by
  // applicationId in KanbanColumn.tsx and so always remounts - but the hook itself should be
  // correct in isolation, not correct by accident of how its only caller happens to use it) could
  // briefly show the wrong application's resume while the new one is loading.
  it("clears the previous application's base resume when applicationId changes", async () => {
    server.use(
      http.get('*/api/JobApplications/10/resume-context', () => HttpResponse.json('Resume for app 10')),
    )
    const { result, rerender } = renderHook(
      ({ id }: { id: number }) => useApplicationReview(id),
      { initialProps: { id: 10 } },
    )
    await waitFor(() => expect(result.current.baseResume).toBe('Resume for app 10'))

    // A pending (never-resolving) handler lets us observe the state right after the id changes,
    // before the new fetch would settle.
    server.use(
      http.get('*/api/JobApplications/20/resume-context', () => new Promise(() => {})),
    )
    rerender({ id: 20 })

    await waitFor(() => expect(result.current.baseResumeLoading).toBe(true))
    expect(result.current.baseResume).toBeNull()
  })

  it('sets an action error when reject fails', async () => {
    server.use(
      http.get('*/api/JobApplications/10/resume-context', () => HttpResponse.json('base')),
      http.post('*/api/JobApplications/10/reject', () => new HttpResponse(null, { status: 400 })),
    )

    const { result } = renderHook(() => useApplicationReview(10))
    await waitFor(() => expect(result.current.baseResumeLoading).toBe(false))

    await act(async () => {
      await result.current.reject('')
    })

    expect(result.current.actionError).not.toBeNull()
    expect(result.current.actionLoading).toBe(false)
  })
})
