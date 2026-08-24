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

  // User report (2026-08-22): the Review-stage controls should let the reviewer inspect the real
  // file in a new tab, not silently save it to disk - a separate 'preview' mode, opt-in via the
  // mode param, so the existing (default) download behavior used elsewhere (Done/Approved tasks)
  // is completely unaffected.
  //
  // PR #65 review finding: window.open() must be called synchronously, before the awaited fetch
  // below - calling it AFTER an await is a well-known popup-blocker trigger (Safari in particular
  // enforces this strictly), since the click's "user activation" window can expire during the
  // await. So a blank window is opened immediately and only navigated to the real blob afterward,
  // rather than opening the already-resolved blob URL in one step.
  it('opens a window immediately (before the fetch resolves) and navigates it to the file when mode is preview', async () => {
    server.use(
      http.get('*/api/JobApplications/10/export/resume', () =>
        new HttpResponse('resume bytes', {
          headers: { 'Content-Disposition': 'attachment; filename="resume.pdf"' },
        })),
    )
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    const fakeWindow = { location: { href: '' }, close: vi.fn() } as unknown as Window
    const openSpy = vi.spyOn(window, 'open').mockReturnValue(fakeWindow)

    const { result } = renderHook(() => useExportDownload(10))

    await act(async () => {
      await result.current.download('resume', 'pdf', 'preview')
    })

    // Called with '' (a blank window), not the blob URL - the URL doesn't exist yet at the point
    // window.open() must be called for the activation window to still be open.
    expect(openSpy).toHaveBeenCalledWith('', '_blank')
    expect(fakeWindow.location.href).toBe('blob:mock-url')
    expect(clickSpy).not.toHaveBeenCalled()
    expect(result.current.error).toBeNull()

    clickSpy.mockRestore()
    openSpy.mockRestore()
  })

  // PR #65 review finding (found independently by two reviewers): window.open()'s return value was
  // never checked - a popup-blocked preview silently did nothing, with no feedback to the reviewer
  // that anything went wrong.
  it('surfaces a clear error when the browser blocks the preview window', async () => {
    server.use(
      http.get('*/api/JobApplications/10/export/resume', () =>
        new HttpResponse('resume bytes', {
          headers: { 'Content-Disposition': 'attachment; filename="resume.pdf"' },
        })),
    )
    const openSpy = vi.spyOn(window, 'open').mockReturnValue(null)

    const { result } = renderHook(() => useExportDownload(10))

    await act(async () => {
      await result.current.download('resume', 'pdf', 'preview')
    })

    expect(result.current.error).toMatch(/blocked the preview/i)

    openSpy.mockRestore()
  })

  // User report (2026-08-24): opened a preview tab, read the resume for a few minutes, then tried
  // to save it from the browser's own PDF viewer - it failed ("Check internet connection", Chrome's
  // generic error for a dead blob: URL). Root cause: a fixed 60-second setTimeout revoked the
  // object URL regardless of whether the user was still using the tab.
  //
  // PR #69 review finding (Antigravity/Gemini, independently confirmed by a second manual review):
  // a first attempt at this fix removed revocation entirely, which is a real, unbounded memory leak
  // - URL.createObjectURL(blob) ties the object URL's lifetime to the *global that created it* (this
  // SPA's main tab), not whatever document win.location.href later navigates the popup to, so
  // closing the preview tab was never going to release it on its own. The fix is to revoke on the
  // real signal (the preview tab actually closing, polled via win.closed - readable cross-origin
  // even once win has navigated to a blob: URL) instead of either a guessed duration or never at all.
  it('does not revoke the preview blob URL while the tab is still open, even after a long time', async () => {
    vi.useFakeTimers()
    server.use(
      http.get('*/api/JobApplications/10/export/resume', () =>
        new HttpResponse('resume bytes', {
          headers: { 'Content-Disposition': 'attachment; filename="resume.pdf"' },
        })),
    )
    const fakeWindow = { location: { href: '' }, close: vi.fn(), closed: false } as unknown as Window
    vi.spyOn(window, 'open').mockReturnValue(fakeWindow)

    const { result } = renderHook(() => useExportDownload(10))

    await act(async () => {
      await result.current.download('resume', 'pdf', 'preview')
    })

    await vi.advanceTimersByTimeAsync(10 * 60 * 1000)

    expect(URL.revokeObjectURL).not.toHaveBeenCalled()

    vi.useRealTimers()
  })

  it('revokes the preview blob URL once the tab is actually closed', async () => {
    vi.useFakeTimers()
    server.use(
      http.get('*/api/JobApplications/10/export/resume', () =>
        new HttpResponse('resume bytes', {
          headers: { 'Content-Disposition': 'attachment; filename="resume.pdf"' },
        })),
    )
    const fakeWindow = { location: { href: '' }, close: vi.fn(), closed: false } as unknown as Window
    vi.spyOn(window, 'open').mockReturnValue(fakeWindow)

    const { result } = renderHook(() => useExportDownload(10))

    await act(async () => {
      await result.current.download('resume', 'pdf', 'preview')
    })

    expect(URL.revokeObjectURL).not.toHaveBeenCalled()

    ;(fakeWindow as unknown as { closed: boolean }).closed = true
    await vi.advanceTimersByTimeAsync(1_000)

    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:mock-url')

    vi.useRealTimers()
  })
})
