import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import { useExportDownload } from './useExportDownload'

// jsdom does not implement the Blob-URL APIs, so stub them per test the way a real browser would
// provide them. HTMLAnchorElement.prototype.click is real in jsdom but triggering its default
// navigation logs "not implemented" noise for a detached anchor - mock it so the test stays quiet
// and so we can assert it was actually called.
beforeEach(() => {
  URL.createObjectURL = vi.fn(() => 'blob:mock-url') as unknown as typeof URL.createObjectURL
  URL.revokeObjectURL = vi.fn()
})

describe('useExportDownload', () => {
  it('downloads the resume and triggers the browser-download sequence', async () => {
    server.use(
      http.get('*/api/JobApplications/10/export/resume', () =>
        new HttpResponse('resume bytes', {
          headers: { 'Content-Disposition': 'attachment; filename="resume.pdf"' },
        })),
    )
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})

    const { result } = renderHook(() => useExportDownload(10))

    await act(async () => {
      await result.current.download('resume', 'pdf')
    })

    expect(URL.createObjectURL).toHaveBeenCalled()
    expect(clickSpy).toHaveBeenCalledOnce()
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:mock-url')
    expect(result.current.error).toBeNull()
    expect(result.current.downloading.size).toBe(0)

    clickSpy.mockRestore()
  })

  it('calls the cover-letter export when kind is coverLetter', async () => {
    let hitCoverLetter = false
    server.use(
      http.get('*/api/JobApplications/10/export/cover-letter', () => {
        hitCoverLetter = true
        return new HttpResponse('cl bytes', {
          headers: { 'Content-Disposition': 'attachment; filename="cover-letter.md"' },
        })
      }),
    )
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})

    const { result } = renderHook(() => useExportDownload(10))

    await act(async () => {
      await result.current.download('coverLetter', 'markdown')
    })

    expect(hitCoverLetter).toBe(true)
    clickSpy.mockRestore()
  })

  it('sets downloading while the request is in flight', async () => {
    // A pending (never-resolving) handler lets us observe the mid-flight state, same trick as
    // useApplicationReview.test.ts uses for its applicationId-change test.
    server.use(
      http.get('*/api/JobApplications/10/export/resume', () => new Promise(() => {})),
    )

    const { result } = renderHook(() => useExportDownload(10))
    expect(result.current.downloading.size).toBe(0)

    act(() => {
      void result.current.download('resume', 'pdf')
    })

    await waitFor(() => expect(result.current.downloading.has('resume-pdf')).toBe(true))
  })

  // PR #48 review finding (found independently and by Copilot): downloading used to be a single
  // string|null, but both buttons for a task (PDF and Markdown) can be clicked before either
  // resolves - whichever finishes first cleared the *shared* state via `finally`, wiping out the
  // other download's still-in-flight indicator even though it was still running. Proven here with
  // two calls sharing one route (resume) distinguished only by the format query param: pdf never
  // resolves, markdown resolves immediately - if the old bug were present, awaiting the markdown
  // download would incorrectly clear the pdf download's in-flight state too.
  it('keeps a different download key in-flight after another one completes', async () => {
    server.use(
      http.get('*/api/JobApplications/10/export/resume', ({ request }) => {
        const format = new URL(request.url).searchParams.get('format')
        if (format === 'markdown') {
          return new HttpResponse('resume md bytes', {
            headers: { 'Content-Disposition': 'attachment; filename="resume.md"' },
          })
        }
        return new Promise(() => {}) // pdf: never resolves
      }),
    )
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})

    const { result } = renderHook(() => useExportDownload(10))

    act(() => {
      void result.current.download('resume', 'pdf')
    })
    await waitFor(() => expect(result.current.downloading.has('resume-pdf')).toBe(true))

    await act(async () => {
      await result.current.download('resume', 'markdown')
    })

    // The still-pending pdf download must still be reported as in-flight - it did not resolve.
    expect(result.current.downloading.has('resume-pdf')).toBe(true)
    expect(result.current.downloading.has('resume-markdown')).toBe(false)

    clickSpy.mockRestore()
  })

  it('sets an error and does not throw when the download fails', async () => {
    server.use(
      http.get('*/api/JobApplications/10/export/resume', () =>
        new HttpResponse('Application not approved', { status: 400 })),
    )

    const { result } = renderHook(() => useExportDownload(10))

    await act(async () => {
      await result.current.download('resume', 'pdf')
    })

    expect(result.current.error).not.toBeNull()
    expect(result.current.downloading.size).toBe(0)
  })
})
