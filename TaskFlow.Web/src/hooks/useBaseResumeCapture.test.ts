import { describe, it, expect } from 'vitest'
import { renderHook, act, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import { useBaseResumeCapture } from './useBaseResumeCapture'

describe('useBaseResumeCapture', () => {
  it('saves the resume content and reports success', async () => {
    const { result } = renderHook(() => useBaseResumeCapture())

    await act(async () => {
      await result.current.save('session-1', 'My resume text')
    })

    await waitFor(() => expect(result.current.saved).toBe(true))
    expect(result.current.loading).toBe(false)
    expect(result.current.error).toBeNull()
  })

  it('surfaces an error and does not report success when the save fails', async () => {
    server.use(
      http.post('*/api/JobApplications/resume-context', () => new HttpResponse(null, { status: 500 })),
    )
    const { result } = renderHook(() => useBaseResumeCapture())

    await act(async () => {
      await result.current.save('session-1', 'My resume text')
    })

    await waitFor(() => expect(result.current.error).not.toBeNull())
    expect(result.current.saved).toBe(false)
    expect(result.current.loading).toBe(false)
  })
})
