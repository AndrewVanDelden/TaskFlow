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
  // User report (2026-08-22): this card was rendering all three artifacts' full raw content
  // inline, making it enormous - many screen-heights tall on the board, next to a compact one-line
  // Done card. The card should be compact like every other card; each artifact gets a "View"
  // control that opens the real content in a new tab, not a wall of text dumped into the card.
  it('shows compact preview controls for all three artifacts, with no raw content rendered inline', async () => {
    server.use(
      http.get('*/api/JobApplications/10/resume-context', () => HttpResponse.json('My base resume text')),
    )

    render(<ApplicationReviewCard applicationId={10} resumeTask={resumeTask} coverLetterTask={coverLetterTask} />)

    expect(await screen.findByRole('button', { name: /view base resume/i })).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /view pdf/i })).toHaveLength(2)
    expect(screen.getAllByRole('button', { name: /view markdown/i })).toHaveLength(2)

    // The whole point of this change: none of the raw content is dumped into the card itself.
    expect(screen.queryByText('My base resume text')).not.toBeInTheDocument()
    expect(screen.queryByText('Some resume content.')).not.toBeInTheDocument()
    expect(screen.queryByText('Dear hiring manager.')).not.toBeInTheDocument()
  })

  // User report (2026-08-24): a generic "Application review" heading gives no clue which
  // application this card is for once several are open at once.
  it('names the heading after the job title and company, not a generic label', async () => {
    server.use(
      http.get('*/api/JobApplications/10/resume-context', () => HttpResponse.json('My base resume text')),
    )
    const namedResumeTask: TaskItem = { ...resumeTask, title: 'Senior Software Engineer', company: 'Acme Corp' }

    render(<ApplicationReviewCard applicationId={10} resumeTask={namedResumeTask} coverLetterTask={coverLetterTask} />)

    expect(screen.getByRole('heading', { name: /application review — senior software engineer — acme corp/i })).toBeInTheDocument()
  })

  it('opens the base resume in a new tab when "View base resume" is clicked', async () => {
    server.use(
      http.get('*/api/JobApplications/10/resume-context', () => HttpResponse.json('My base resume text')),
    )
    const writeSpy = vi.fn()
    const fakeWindow = { document: { write: writeSpy, close: vi.fn() } } as unknown as Window
    const openSpy = vi.spyOn(window, 'open').mockReturnValue(fakeWindow)

    render(<ApplicationReviewCard applicationId={10} resumeTask={resumeTask} coverLetterTask={coverLetterTask} />)
    await userEvent.click(await screen.findByRole('button', { name: /view base resume/i }))

    expect(openSpy).toHaveBeenCalledWith('', '_blank')
    expect(writeSpy).toHaveBeenCalledWith(expect.stringContaining('My base resume text'))

    openSpy.mockRestore()
  })

  it('shows an error when the browser blocks the base resume preview window', async () => {
    server.use(
      http.get('*/api/JobApplications/10/resume-context', () => HttpResponse.json('My base resume text')),
    )
    const openSpy = vi.spyOn(window, 'open').mockReturnValue(null)

    render(<ApplicationReviewCard applicationId={10} resumeTask={resumeTask} coverLetterTask={coverLetterTask} />)
    await userEvent.click(await screen.findByRole('button', { name: /view base resume/i }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(/blocked the preview/i)

    openSpy.mockRestore()
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
    await screen.findByRole('button', { name: /view base resume/i })

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
    await screen.findByRole('button', { name: /view base resume/i })

    await userEvent.type(screen.getByPlaceholderText(/reason/i), 'Needs more detail')
    await userEvent.click(screen.getByRole('button', { name: 'Reject' }))

    await waitFor(() => expect(capturedBody).toEqual({ reason: 'Needs more detail' }))
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
    await screen.findByRole('button', { name: /view base resume/i })

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
    await screen.findByRole('button', { name: /view base resume/i })

    await userEvent.click(screen.getByRole('button', { name: 'Approve' }))

    const alert = await screen.findByRole('alert')
    expect(alert).not.toHaveTextContent('')
  })
})
