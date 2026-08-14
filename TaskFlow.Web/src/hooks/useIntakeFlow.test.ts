import { describe, it, expect } from 'vitest'
import { renderHook, act, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import { useIntakeFlow } from './useIntakeFlow'

describe('useIntakeFlow', () => {
  it('parse() moves provide -> parsing -> review and populates drafts on success', async () => {
    const { result } = renderHook(() => useIntakeFlow('session-1'))
    expect(result.current.stage).toBe('provide')

    act(() => {
      result.current.setJobPostingText('Backend Engineer job posting text')
    })

    await act(async () => {
      await result.current.parse()
    })

    await waitFor(() => expect(result.current.stage).toBe('review'))
    expect(result.current.drafts).toHaveLength(1)
    expect(result.current.drafts[0].title).toBe('Backend Engineer')
    expect(result.current.error).toBeNull()
  })

  // PR #49 review finding (found independently and by Copilot): the parser can legitimately
  // resolve successfully with an empty array (no Anthropic key configured, and/or the posting has
  // no markdown heading the free parser recognizes - both ordinary, expected states, not server
  // errors). Before the fix, this moved straight to 'review' with an empty drafts array, and
  // startTailoring() would later crash dereferencing drafts[0] after already saving the resume as
  // a side effect. An empty result must be treated the same as a parse failure.
  it('parse() reverts to provide with an error when the server returns an empty draft list', async () => {
    server.use(
      http.post('*/api/JobApplications/parse', () => HttpResponse.json([])),
    )
    const { result } = renderHook(() => useIntakeFlow('session-1'))

    act(() => {
      result.current.setJobPostingText('Some text with no recognizable job title')
    })

    await act(async () => {
      await result.current.parse()
    })

    await waitFor(() => expect(result.current.stage).toBe('provide'))
    expect(result.current.error).not.toBeNull()
    expect(result.current.drafts).toHaveLength(0)
  })

  it('parse() reverts to provide with an error on failure', async () => {
    server.use(
      http.post('*/api/JobApplications/parse', () => new HttpResponse(null, { status: 500 })),
    )
    const { result } = renderHook(() => useIntakeFlow('session-1'))

    act(() => {
      result.current.setJobPostingText('Backend Engineer job posting text')
    })

    await act(async () => {
      await result.current.parse()
    })

    await waitFor(() => expect(result.current.stage).toBe('provide'))
    expect(result.current.error).not.toBeNull()
  })

  // Gets the hook to the 'review' stage via a real, successful parse() call, since useIntakeFlow
  // owns the transition itself rather than exposing a way to set stage directly.
  async function reachReview(sessionId = 'session-1') {
    const { result } = renderHook(() => useIntakeFlow(sessionId))
    act(() => {
      result.current.setJobPostingText('Backend Engineer job posting text')
    })
    await act(async () => {
      await result.current.parse()
    })
    await waitFor(() => expect(result.current.stage).toBe('review'))
    return result
  }

  it('startTailoring() saves the resume, assembles, and moves to building with both task ids captured', async () => {
    const result = await reachReview()

    act(() => {
      result.current.setBaseResumeText('My base resume text')
    })

    await act(async () => {
      await result.current.startTailoring()
    })

    await waitFor(() => expect(result.current.stage).toBe('building'))
    expect(result.current.applicationId).toBe(1)
    expect(result.current.resumeTaskId).toBe(101)
    expect(result.current.coverLetterTaskId).toBe(102)
    expect(result.current.error).toBeNull()
  })

  it('startTailoring() reverts to review with an error if assembling fails', async () => {
    const result = await reachReview()

    act(() => {
      result.current.setBaseResumeText('My base resume text')
    })

    server.use(
      http.post('*/api/JobApplications', () => new HttpResponse(null, { status: 404 })),
    )

    await act(async () => {
      await result.current.startTailoring()
    })

    await waitFor(() => expect(result.current.stage).toBe('review'))
    expect(result.current.error).not.toBeNull()
  })

  // Epic 3.1, U3.2: threads TaskDraft.company through to the assemble call.
  it('startTailoring() sends the parsed draft\'s company on the assemble call', async () => {
    server.use(
      http.post('*/api/JobApplications/parse', () =>
        HttpResponse.json([
          { title: 'Backend Engineer', description: 'Build things.', kind: 'ResumeTailoring', section: 'Job Posting', company: 'Acme Corp' },
        ])),
    )
    const result = await reachReview()

    act(() => {
      result.current.setBaseResumeText('My base resume text')
    })

    let capturedBody: unknown = null
    server.use(
      http.post('*/api/JobApplications', async ({ request }) => {
        capturedBody = await request.json()
        return HttpResponse.json({
          id: 1, state: 'Building', ingestionSessionId: '', ownerId: 1, createdAt: '',
          tasks: [
            { id: 101, title: 'Tailor resume', kind: 'ResumeTailoring', status: 'Todo' },
            { id: 102, title: 'Cover letter', kind: 'CoverLetterTailoring', status: 'Todo' },
          ],
        })
      }),
    )

    await act(async () => {
      await result.current.startTailoring()
    })

    await waitFor(() => expect(result.current.stage).toBe('building'))
    expect((capturedBody as { posting: { company: string | null } }).posting.company).toBe('Acme Corp')
  })

  // Epic 3.1, U3.2: a draft with no parsed company (undefined, per the default parse handler)
  // must normalize to null on the wire, not be sent as undefined (which JSON.stringify would drop).
  it('startTailoring() normalizes an absent draft company to null on the assemble call', async () => {
    const result = await reachReview()

    act(() => {
      result.current.setBaseResumeText('My base resume text')
    })

    let capturedBody: unknown = null
    server.use(
      http.post('*/api/JobApplications', async ({ request }) => {
        capturedBody = await request.json()
        return HttpResponse.json({
          id: 1, state: 'Building', ingestionSessionId: '', ownerId: 1, createdAt: '',
          tasks: [
            { id: 101, title: 'Tailor resume', kind: 'ResumeTailoring', status: 'Todo' },
            { id: 102, title: 'Cover letter', kind: 'CoverLetterTailoring', status: 'Todo' },
          ],
        })
      }),
    )

    expect(result.current.drafts[0].company).toBeUndefined()

    await act(async () => {
      await result.current.startTailoring()
    })

    await waitFor(() => expect(result.current.stage).toBe('building'))
    expect((capturedBody as { posting: { company: string | null } }).posting.company).toBeNull()
  })
})
