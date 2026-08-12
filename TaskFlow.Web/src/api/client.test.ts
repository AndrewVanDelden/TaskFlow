import { describe, it, expect } from 'vitest'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import { setToken, getToken, requestBlob } from './client'
import { getTasks } from './tasks'

describe('401 handling', () => {
  it('clears the stored token when a protected call returns 401', async () => {
    setToken('stale.token')

    // Override just for this test: make /api/Tasks answer 401.
    server.use(
      http.get('*/api/Tasks', () => new HttpResponse(null, { status: 401 })),
    )

    await expect(getTasks()).rejects.toThrow() // the call fails
    expect(getToken()).toBeNull() // and the token was cleared
  })
})

describe('requestBlob', () => {
  it('attaches the Authorization header when a token exists', async () => {
    setToken('a.b.c')
    let capturedAuth: string | null = null
    server.use(
      http.get('*/api/JobApplications/10/export/resume', ({ request }) => {
        capturedAuth = request.headers.get('Authorization')
        return new HttpResponse('file bytes', {
          headers: { 'Content-Disposition': 'attachment; filename="resume.pdf"' },
        })
      }),
    )

    await requestBlob('/api/JobApplications/10/export/resume?format=pdf')

    expect(capturedAuth).toBe('Bearer a.b.c')
  })

  it('returns the blob and the filename extracted from Content-Disposition', async () => {
    server.use(
      http.get('*/api/JobApplications/10/export/resume', () =>
        new HttpResponse('file bytes', {
          headers: {
            'Content-Disposition': 'attachment; filename="resume.pdf"',
            'Content-Type': 'text/markdown; charset=utf-8',
          },
        })),
    )

    const result = await requestBlob('/api/JobApplications/10/export/resume?format=markdown')

    expect(result.filename).toBe('resume.pdf')
    expect(await result.blob.text()).toBe('file bytes')
  })

  it('falls back to a sensible filename when Content-Disposition is missing', async () => {
    server.use(
      http.get('*/api/JobApplications/10/export/resume', () => new HttpResponse('file bytes')),
    )

    const result = await requestBlob('/api/JobApplications/10/export/resume?format=pdf')

    expect(result.filename).toBe('download')
  })

  it('throws the same ApiError shape (status + message) as request() on a non-OK response', async () => {
    server.use(
      http.get('*/api/JobApplications/10/export/resume', () =>
        new HttpResponse('Application not approved', { status: 400 })),
    )

    await expect(requestBlob('/api/JobApplications/10/export/resume?format=pdf')).rejects.toMatchObject({
      status: 400,
      message: 'Application not approved',
    })
  })

  it('clears the token and throws on 401, same as request()', async () => {
    setToken('stale.token')
    server.use(
      http.get('*/api/JobApplications/10/export/resume', () => new HttpResponse(null, { status: 401 })),
    )

    await expect(requestBlob('/api/JobApplications/10/export/resume?format=pdf')).rejects.toThrow()
    expect(getToken()).toBeNull()
  })

  // Copilot review finding (PR #48): real server error responses (from ToActionResult /
  // ToFileActionResult) are JSON objects shaped { "message": "..." }, but the test above uses a
  // plain-text body that doesn't match that real shape - it was masking that the raw JSON string
  // was being used verbatim as ApiError.message, so a failed export showed literal JSON text in
  // the UI's error alert instead of the actual message.
  it('extracts the message field from a JSON error body, matching the real server response shape', async () => {
    server.use(
      http.get('*/api/JobApplications/10/export/resume', () =>
        HttpResponse.json({ message: 'JobApplication 10 is ReviewReady; only Approved applications can be exported.' }, { status: 400 })),
    )

    await expect(requestBlob('/api/JobApplications/10/export/resume?format=pdf')).rejects.toMatchObject({
      status: 400,
      message: 'JobApplication 10 is ReviewReady; only Approved applications can be exported.',
    })
  })
})
