import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import { ApplicationReviewCard } from './ApplicationReviewCard'
import type { TaskItem } from '../types'

const resumeTask: TaskItem = {
  id: 1,
  title: 'Tailor resume',
  description: null,
  status: 'Review',
  priority: 'High',
  dueDate: null,
  createdAt: '',
  updatedAt: '',
  assignedToId: null,
  assignedToName: null,
  kind: 'ResumeTailoring',
  applicationId: 10,
  tailoredContent: '# Tailored Resume\n\nSome resume content.',
}

const coverLetterTask: TaskItem = {
  ...resumeTask,
  id: 2,
  title: 'Tailor cover letter',
  kind: 'CoverLetterTailoring',
  tailoredContent: '# Tailored Cover Letter\n\nDear hiring manager.',
}

describe('ApplicationReviewCard', () => {
  it('renders the base resume, tailored resume, and cover letter markdown sections', async () => {
    server.use(
      http.get('*/api/JobApplications/10/resume-context', () => HttpResponse.json('My base resume')),
    )

    render(<ApplicationReviewCard applicationId={10} resumeTask={resumeTask} coverLetterTask={coverLetterTask} />)

    expect(await screen.findByText('My base resume')).toBeInTheDocument()
    expect(screen.getByText('Tailored Resume')).toBeInTheDocument()
    expect(screen.getByText('Tailored Cover Letter')).toBeInTheDocument()
  })

  it('renders a script-tag payload inert', async () => {
    server.use(
      http.get('*/api/JobApplications/10/resume-context', () => HttpResponse.json('base')),
    )
    const withScript: TaskItem = {
      ...resumeTask,
      tailoredContent: "# Title\n\n<script>alert('xss')</script>\n\nSafe text.",
    }

    const { container } = render(
      <ApplicationReviewCard applicationId={10} resumeTask={withScript} coverLetterTask={coverLetterTask} />,
    )

    await screen.findByText('Safe text.')
    expect(container.querySelector('script')).toBeNull()
    expect(container.innerHTML).not.toContain('<script>')
  })

  it('approves and shows a success indication', async () => {
    server.use(
      http.get('*/api/JobApplications/10/resume-context', () => HttpResponse.json('base')),
      http.post('*/api/JobApplications/10/approve', () =>
        HttpResponse.json({
          id: 10, state: 'Approved', ingestionSessionId: '', ownerId: 1, createdAt: '', tasks: [],
        })),
    )

    render(<ApplicationReviewCard applicationId={10} resumeTask={resumeTask} coverLetterTask={coverLetterTask} />)
    await screen.findByText('base')

    await userEvent.click(screen.getByRole('button', { name: 'Approve' }))

    expect(await screen.findByText(/approved/i)).toBeInTheDocument()
  })

  it('rejects with a typed reason', async () => {
    let capturedBody: unknown = null
    server.use(
      http.get('*/api/JobApplications/10/resume-context', () => HttpResponse.json('base')),
      http.post('*/api/JobApplications/10/reject', async ({ request }) => {
        capturedBody = await request.json()
        return HttpResponse.json({
          id: 10, state: 'Building', ingestionSessionId: '', ownerId: 1, createdAt: '', tasks: [],
        })
      }),
    )

    render(<ApplicationReviewCard applicationId={10} resumeTask={resumeTask} coverLetterTask={coverLetterTask} />)
    await screen.findByText('base')

    await userEvent.type(screen.getByPlaceholderText(/reason/i), 'Needs more detail')
    await userEvent.click(screen.getByRole('button', { name: 'Reject' }))

    await waitFor(() => expect(capturedBody).toEqual({ reason: 'Needs more detail' }))
  })

  // User report (2026-08-22): a wall of raw markdown text isn't enough to judge real output - the
  // user wants to open the actual PDF (or Markdown) file for each artifact in a new tab, exactly
  // as it will really look, before deciding to approve or reject. "View", not "Download": these
  // controls use ExportDownloadControls' preview mode, which opens a new tab instead of saving a
  // copy to disk.
  it('shows PDF/Markdown preview controls for both the resume and cover letter', async () => {
    server.use(
      http.get('*/api/JobApplications/10/resume-context', () => HttpResponse.json('base')),
    )

    render(<ApplicationReviewCard applicationId={10} resumeTask={resumeTask} coverLetterTask={coverLetterTask} />)
    await screen.findByText('base')

    expect(screen.getAllByRole('button', { name: /view pdf/i })).toHaveLength(2)
    expect(screen.getAllByRole('button', { name: /view markdown/i })).toHaveLength(2)
  })

  it('opens the resume PDF in a new tab instead of downloading it', async () => {
    server.use(
      http.get('*/api/JobApplications/10/resume-context', () => HttpResponse.json('base')),
      http.get('*/api/JobApplications/10/export/resume', () =>
        new HttpResponse('file bytes', {
          headers: { 'Content-Disposition': 'attachment; filename="resume.pdf"' },
        })),
    )
    // A realistic (unblocked) window, not null - null means "blocked", a separate case covered in
    // useExportDownload.test.ts, not what this test is proving. jsdom implements createObjectURL
    // for real (unlike the other export-related test files, which stub it), so it's stubbed here
    // too for a predictable, assertable URL value.
    const createObjectURLSpy = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url')
    const fakeWindow = { location: { href: '' }, close: vi.fn() } as unknown as Window
    const openSpy = vi.spyOn(window, 'open').mockReturnValue(fakeWindow)
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})

    render(<ApplicationReviewCard applicationId={10} resumeTask={resumeTask} coverLetterTask={coverLetterTask} />)
    await screen.findByText('base')

    await userEvent.click(screen.getAllByRole('button', { name: /view pdf/i })[0])

    await waitFor(() => expect(fakeWindow.location.href).toBe('blob:mock-url'))
    expect(clickSpy).not.toHaveBeenCalled()

    openSpy.mockRestore()
    clickSpy.mockRestore()
    createObjectURLSpy.mockRestore()
  })

  it('shows an error message and does not silently swallow a failed approve', async () => {
    server.use(
      http.get('*/api/JobApplications/10/resume-context', () => HttpResponse.json('base')),
      http.post('*/api/JobApplications/10/approve', () => new HttpResponse(null, { status: 409 })),
    )

    render(<ApplicationReviewCard applicationId={10} resumeTask={resumeTask} coverLetterTask={coverLetterTask} />)
    await screen.findByText('base')

    await userEvent.click(screen.getByRole('button', { name: 'Approve' }))

    const alert = await screen.findByRole('alert')
    expect(alert).not.toHaveTextContent('')
  })
})
