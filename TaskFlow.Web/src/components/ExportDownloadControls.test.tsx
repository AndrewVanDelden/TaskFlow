import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import { ExportDownloadControls } from './ExportDownloadControls'

beforeEach(() => {
  URL.createObjectURL = vi.fn(() => 'blob:mock-url') as unknown as typeof URL.createObjectURL
  URL.revokeObjectURL = vi.fn()
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
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
})
