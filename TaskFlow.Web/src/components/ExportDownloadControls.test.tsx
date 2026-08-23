import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import { ExportDownloadControls } from './ExportDownloadControls'

beforeEach(() => {
  URL.createObjectURL = vi.fn(() => 'blob:mock-url') as unknown as typeof URL.createObjectURL
  URL.revokeObjectURL = vi.fn()
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
})

// Without this, HTMLAnchorElement.prototype.click's spy above is never torn down between tests, so
// its call count silently accumulates across every test in this file - found via the new preview
// test below, the first one to assert the negative case (.not.toHaveBeenCalled()).
afterEach(() => {
  vi.restoreAllMocks()
})

describe('ExportDownloadControls', () => {
  it('calls the resume export with format=pdf when Download PDF is clicked for a resume task', async () => {
    let capturedUrl = ''
    server.use(
      http.get('*/api/JobApplications/10/export/resume', ({ request }) => {
        capturedUrl = request.url
        return new HttpResponse('file bytes', {
          headers: { 'Content-Disposition': 'attachment; filename="resume.pdf"' },
        })
      }),
    )

    render(<ExportDownloadControls applicationId={10} kind="ResumeTailoring" />)
    await userEvent.click(screen.getByRole('button', { name: /download pdf/i }))

    await waitFor(() => expect(capturedUrl).toContain('format=pdf'))
    expect(capturedUrl).toContain('/api/JobApplications/10/export/resume')
  })

  it('calls the resume export with format=markdown when Download Markdown is clicked', async () => {
    let capturedUrl = ''
    server.use(
      http.get('*/api/JobApplications/10/export/resume', ({ request }) => {
        capturedUrl = request.url
        return new HttpResponse('file bytes', {
          headers: { 'Content-Disposition': 'attachment; filename="resume.md"' },
        })
      }),
    )

    render(<ExportDownloadControls applicationId={10} kind="ResumeTailoring" />)
    await userEvent.click(screen.getByRole('button', { name: /download markdown/i }))

    await waitFor(() => expect(capturedUrl).toContain('format=markdown'))
  })

  it('calls the cover-letter export (not the resume export) when kind is CoverLetterTailoring', async () => {
    let hitCoverLetter = false
    let hitResume = false
    server.use(
      http.get('*/api/JobApplications/10/export/cover-letter', () => {
        hitCoverLetter = true
        return new HttpResponse('file bytes', {
          headers: { 'Content-Disposition': 'attachment; filename="cl.pdf"' },
        })
      }),
      http.get('*/api/JobApplications/10/export/resume', () => {
        hitResume = true
        return new HttpResponse('file bytes', {
          headers: { 'Content-Disposition': 'attachment; filename="resume.pdf"' },
        })
      }),
    )

    render(<ExportDownloadControls applicationId={10} kind="CoverLetterTailoring" />)
    await userEvent.click(screen.getByRole('button', { name: /download pdf/i }))

    await waitFor(() => expect(hitCoverLetter).toBe(true))
    expect(hitResume).toBe(false)
  })

  it('shows an alert and does not crash when a download fails', async () => {
    server.use(
      http.get('*/api/JobApplications/10/export/resume', () =>
        new HttpResponse('Application not approved', { status: 400 })),
    )

    render(<ExportDownloadControls applicationId={10} kind="ResumeTailoring" />)
    await userEvent.click(screen.getByRole('button', { name: /download pdf/i }))

    const alert = await screen.findByRole('alert')
    expect(alert).not.toHaveTextContent('')
  })

  // User report (2026-08-22): Review-stage controls open the file in a new tab (mode="preview"),
  // not a disk download - button labels reflect that ("View", not "Download") and no anchor-click
  // download is triggered.
  it('shows "View" labels and opens a new tab instead of downloading when mode is preview', async () => {
    server.use(
      http.get('*/api/JobApplications/10/export/resume', () =>
        new HttpResponse('file bytes', {
          headers: { 'Content-Disposition': 'attachment; filename="resume.pdf"' },
        })),
    )
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    // A realistic (unblocked) window, not null - null now means "blocked", which is its own
    // dedicated case in useExportDownload.test.ts, not what this test is proving.
    const fakeWindow = { location: { href: '' }, close: vi.fn() } as unknown as Window
    const openSpy = vi.spyOn(window, 'open').mockReturnValue(fakeWindow)

    render(<ExportDownloadControls applicationId={10} kind="ResumeTailoring" mode="preview" />)
    await userEvent.click(screen.getByRole('button', { name: /view pdf/i }))

    await waitFor(() => expect(fakeWindow.location.href).toBe('blob:mock-url'))
    expect(clickSpy).not.toHaveBeenCalled()

    clickSpy.mockRestore()
    openSpy.mockRestore()
  })
})
