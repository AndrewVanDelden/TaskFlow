import { describe, it, expect } from 'vitest'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import {
  saveResumeContext,
  getApplicationResumeContext,
  approveApplication,
  rejectApplication,
  exportResume,
  exportCoverLetter,
} from './jobApplications'

describe('saveResumeContext', () => {
  it('posts the ingestion session id and content, and returns the boolean result', async () => {
    const result = await saveResumeContext('11111111-1111-1111-1111-111111111111', 'My resume text')

    expect(result).toBe(true)
  })
})

// taskStatus is deliberately a separate value from the application's own state (Copilot's
// automated review, PR #45 round 2): a task's status ("Done"/"Todo") and a JobApplication's state
// ("Approved"/"Building") are different vocabularies, and reusing one for the other here would
// mask a bug if a later test starts asserting on the returned task statuses.
const applicationResponse = (state: string, taskStatus: string) => ({
  id: 10,
  state,
  ingestionSessionId: '11111111-1111-1111-1111-111111111111',
  ownerId: 1,
  createdAt: '',
  tasks: [{ id: 1, title: 'Tailor resume', kind: 'ResumeTailoring', status: taskStatus }],
})

describe('getApplicationResumeContext', () => {
  it('gets the base resume text for the application', async () => {
    server.use(
      http.get('*/api/JobApplications/10/resume-context', () => HttpResponse.json('My base resume')),
    )

    const result = await getApplicationResumeContext(10)

    expect(result).toBe('My base resume')
  })

  it('rejects on 404 (not found / not owned / no resume saved)', async () => {
    server.use(
      http.get('*/api/JobApplications/10/resume-context', () => new HttpResponse(null, { status: 404 })),
    )

    await expect(getApplicationResumeContext(10)).rejects.toThrow()
  })
})

describe('approveApplication', () => {
  it('posts approve and returns the application in its Approved state', async () => {
    server.use(
      http.post('*/api/JobApplications/10/approve', () => HttpResponse.json(applicationResponse('Approved', 'Done'))),
    )

    const result = await approveApplication(10)

    expect(result.state).toBe('Approved')
    expect(result.tasks[0].status).toBe('Done')
  })

  it('rejects when the server refuses the approval', async () => {
    server.use(
      http.post('*/api/JobApplications/10/approve', () => new HttpResponse(null, { status: 409 })),
    )

    await expect(approveApplication(10)).rejects.toThrow()
  })
})

describe('rejectApplication', () => {
  it('posts the reason and returns the application in its Building state', async () => {
    server.use(
      http.post('*/api/JobApplications/10/reject', () => HttpResponse.json(applicationResponse('Building', 'Todo'))),
    )

    const result = await rejectApplication(10, 'Needs more detail')

    expect(result.state).toBe('Building')
    expect(result.tasks[0].status).toBe('Todo')
  })

  it('rejects when the reason is empty/whitespace (400)', async () => {
    server.use(
      http.post('*/api/JobApplications/10/reject', () => new HttpResponse(null, { status: 400 })),
    )

    await expect(rejectApplication(10, '   ')).rejects.toThrow()
  })
})

describe('exportResume', () => {
  it('requests the resume export with format=pdf', async () => {
    let capturedUrl = ''
    server.use(
      http.get('*/api/JobApplications/10/export/resume', ({ request }) => {
        capturedUrl = request.url
        return new HttpResponse('file bytes', {
          headers: { 'Content-Disposition': 'attachment; filename="resume.pdf"' },
        })
      }),
    )

    const result = await exportResume(10, 'pdf')

    expect(capturedUrl).toContain('/api/JobApplications/10/export/resume')
    expect(capturedUrl).toContain('format=pdf')
    expect(result.filename).toBe('resume.pdf')
  })

  it('requests the resume export with format=markdown', async () => {
    let capturedUrl = ''
    server.use(
      http.get('*/api/JobApplications/10/export/resume', ({ request }) => {
        capturedUrl = request.url
        return new HttpResponse('file bytes', {
          headers: { 'Content-Disposition': 'attachment; filename="resume.md"' },
        })
      }),
    )

    await exportResume(10, 'markdown')

    expect(capturedUrl).toContain('format=markdown')
  })
})

describe('exportCoverLetter', () => {
  it('requests the cover-letter export with format=pdf', async () => {
    let capturedUrl = ''
    server.use(
      http.get('*/api/JobApplications/10/export/cover-letter', ({ request }) => {
        capturedUrl = request.url
        return new HttpResponse('file bytes', {
          headers: { 'Content-Disposition': 'attachment; filename="cover-letter.pdf"' },
        })
      }),
    )

    const result = await exportCoverLetter(10, 'pdf')

    expect(capturedUrl).toContain('/api/JobApplications/10/export/cover-letter')
    expect(capturedUrl).toContain('format=pdf')
    expect(result.filename).toBe('cover-letter.pdf')
  })

  it('requests the cover-letter export with format=markdown', async () => {
    let capturedUrl = ''
    server.use(
      http.get('*/api/JobApplications/10/export/cover-letter', ({ request }) => {
        capturedUrl = request.url
        return new HttpResponse('file bytes', {
          headers: { 'Content-Disposition': 'attachment; filename="cover-letter.md"' },
        })
      }),
    )

    await exportCoverLetter(10, 'markdown')

    expect(capturedUrl).toContain('format=markdown')
  })
})
