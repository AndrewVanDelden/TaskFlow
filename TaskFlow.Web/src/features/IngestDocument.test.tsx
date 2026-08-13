import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import { http, HttpResponse } from 'msw'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { server } from '../test/server'
import { IngestDocument } from './IngestDocument'

// IngestDocument reads its ingestion session id from the :sessionId route param (App.tsx's
// /ingest/:sessionId route), so every render needs a routing wrapper that supplies one.
function renderIngestDocument() {
  return render(
    <MemoryRouter initialEntries={['/ingest/test-session-id']}>
      <Routes>
        <Route path="/ingest/:sessionId" element={<IngestDocument />} />
      </Routes>
    </MemoryRouter>,
  )
}

// The old generic paste/file/parse/approve flow now lives under a collapsed native <details>
// (Sprint 6). jsdom keeps a closed <details>'s children in the DOM and text/label queries find
// them regardless, but the summary still needs a real click to reach the realistic user flow, so
// every generic-flow test expands it first.
async function expandGenericFlow() {
  await userEvent.click(screen.getByText(/other: paste a generic document/i))
}

describe('IngestDocument - generic document flow (kept, moved under a collapsed <details>)', () => {
  it('parses pasted text and shows the returned drafts', async () => {
    renderIngestDocument()
    await expandGenericFlow()

    await userEvent.type(screen.getByPlaceholderText('Paste a document'), '# doc')
    await userEvent.click(screen.getByRole('button', { name: /^parse$/i }))

    expect(await screen.findByText('Draft from server')).toBeInTheDocument()
  })

  it('approves the previewed drafts and reports how many were added', async () => {
    renderIngestDocument()
    await expandGenericFlow()

    await userEvent.type(screen.getByPlaceholderText('Paste a document'), '# doc')
    await userEvent.click(screen.getByRole('button', { name: /^parse$/i }))
    await screen.findByText('Draft from server')

    await userEvent.click(screen.getByRole('button', { name: /approve/i }))

    expect(await screen.findByText(/added 1 task to the board/i)).toBeInTheDocument()
  })

  // Copilot review finding (PR #49): this textarea only had a placeholder (which disappears once
  // typed and isn't a substitute for an accessible name), unlike the job-posting textarea in the
  // same file which has a real <label>. T6.5, this same sprint, requires labelled inputs.
  it('the paste-a-document textarea has a persistent accessible label', async () => {
    renderIngestDocument()
    await expandGenericFlow()

    expect(screen.getByLabelText(/paste a document/i)).toBeInTheDocument()
  })

  // Copilot review finding (PR #49): this file input had no id/label at all, unlike the
  // job-posting file input in the same file ("Or upload a file"). Distinct label text (not just
  // "upload a file" again) so the two remain unambiguous to getByLabelText - both file inputs are
  // simultaneously present at this point, since the provide-stage job-posting section is still open.
  it('the generic-document file input has an accessible label', async () => {
    renderIngestDocument()
    await expandGenericFlow()

    expect(screen.getByLabelText(/upload a document file/i)).toBeInTheDocument()
  })
})

describe('IngestDocument - base resume capture (kept, unchanged behavior)', () => {
  it('saves the base resume and shows a confirmation message', async () => {
    renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/base resume/i), 'My resume text')
    await userEvent.click(screen.getByRole('button', { name: /save base resume/i }))

    expect(await screen.findByText(/base resume saved/i)).toBeInTheDocument()
  })

  it('reuses the same ingestion session id across multiple base-resume saves', async () => {
    const capturedBodies: Array<{ ingestionSessionId: string; content: string }> = []
    server.use(
      http.post('*/api/JobApplications/resume-context', async ({ request }) => {
        const body = (await request.json()) as { ingestionSessionId: string; content: string }
        capturedBodies.push(body)
        return HttpResponse.json(true)
      }),
    )

    renderIngestDocument()

    const field = screen.getByLabelText(/base resume/i)
    const saveBtn = screen.getByRole('button', { name: /save base resume/i })

    await userEvent.type(field, 'First draft')
    await userEvent.click(saveBtn)
    await screen.findByText(/base resume saved/i)

    await userEvent.type(field, ' plus more')
    await userEvent.click(saveBtn)
    await screen.findByText(/base resume saved/i)

    expect(capturedBodies).toHaveLength(2)
    expect(capturedBodies[0].ingestionSessionId).toBe(capturedBodies[1].ingestionSessionId)
    expect(capturedBodies[0].ingestionSessionId).toBeTruthy()
  })

  it('never writes to localStorage while saving the base resume', async () => {
    const setItemSpy = vi.spyOn(Storage.prototype, 'setItem')

    renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/base resume/i), 'Secret resume contents')
    await userEvent.click(screen.getByRole('button', { name: /save base resume/i }))
    await screen.findByText(/base resume saved/i)

    expect(setItemSpy).not.toHaveBeenCalled()

    setItemSpy.mockRestore()
  })
})

describe('IngestDocument - guided job-application flow (Sprint 6)', () => {
  it('renders labeled job-posting and base-resume inputs at the provide stage', () => {
    renderIngestDocument()

    expect(screen.getByLabelText(/^job posting$/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/base resume/i)).toBeInTheDocument()
  })

  it('parsing a job posting shows the drafts and collapses the job-posting input to a summary', async () => {
    renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/^job posting$/i), 'Backend Engineer job posting text')
    await userEvent.click(screen.getByRole('button', { name: /parse posting/i }))

    expect(await screen.findByText(/job posting:\s*Backend Engineer/i)).toBeInTheDocument()
    expect(screen.queryByLabelText(/^job posting$/i)).not.toBeInTheDocument()
  })

  // Copilot review finding (PR #49): T6.2 says "review drafts" - the review-stage summary must let
  // the user actually verify what the parser extracted (company and requirements), not just the
  // title, before they commit to starting tailoring against it.
  it('the review-stage summary shows the parsed company and requirements, not just the title', async () => {
    renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/^job posting$/i), 'Backend Engineer job posting text')
    await userEvent.click(screen.getByRole('button', { name: /parse posting/i }))
    await screen.findByText(/job posting:\s*Backend Engineer/i)

    expect(screen.getByText('Job Posting')).toBeInTheDocument()
    expect(screen.getByText('Build things.')).toBeInTheDocument()
  })

  it('offers to reuse a previously saved base resume when one exists', async () => {
    server.use(
      http.get('*/api/JobApplications/resume-context/latest', () =>
        HttpResponse.json({ content: 'Reusable resume text', contentFormat: 'text', updatedAt: '2026-08-01T00:00:00Z' })),
    )

    renderIngestDocument()

    const reuseButton = await screen.findByRole('button', { name: /use previously saved base resume/i })
    await userEvent.click(reuseButton)

    expect(screen.getByLabelText(/base resume/i)).toHaveValue('Reusable resume text')
  })

  it('starting tailoring after parsing moves to the building stage', async () => {
    renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/^job posting$/i), 'Backend Engineer job posting text')
    await userEvent.click(screen.getByRole('button', { name: /parse posting/i }))
    await screen.findByText(/job posting:\s*Backend Engineer/i)

    await userEvent.type(screen.getByLabelText(/base resume/i), 'My base resume text')
    await userEvent.click(screen.getByRole('button', { name: /start tailoring/i }))

    expect(await screen.findByText(/tailored resume and cover letter are being generated/i)).toBeInTheDocument()
  })

  it('moves focus to the primary heading on every stage transition (T6.5)', async () => {
    renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/^job posting$/i), 'Backend Engineer job posting text')
    await userEvent.click(screen.getByRole('button', { name: /parse posting/i }))
    await screen.findByText(/job posting:\s*Backend Engineer/i)

    expect(screen.getByRole('heading', { level: 1 })).toHaveFocus()
  })

  // Copilot review finding (PR #49): the heading above is programmatically focused on every stage
  // transition, but outline-none with no replacement ring leaves a keyboard user with no visible
  // indication focus moved there at all - directly contradicts T6.5's own "visible focus"
  // requirement. jsdom doesn't compute real focus-visible styles, so this asserts the same
  // focus-visible:ring utility class this file already uses on every other interactive element.
  it('the stage heading keeps a visible focus ring when focused (T6.5)', () => {
    renderIngestDocument()

    expect(screen.getByRole('heading', { level: 1 }).className).toMatch(/focus-visible:ring/)
  })

  it('renders live per-item progress rows once the building stage is reached (T6.3)', async () => {
    renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/^job posting$/i), 'Backend Engineer job posting text')
    await userEvent.click(screen.getByRole('button', { name: /parse posting/i }))
    await screen.findByText(/job posting:\s*Backend Engineer/i)

    await userEvent.type(screen.getByLabelText(/base resume/i), 'My base resume text')
    await userEvent.click(screen.getByRole('button', { name: /start tailoring/i }))
    await screen.findByText(/tailored resume and cover letter are being generated/i)

    // Default MSW handlers produce no AgentLog entries for these fresh task ids, so both rows
    // render their initial 'pending'/'in-progress'-shaped state - proof the component is wired in,
    // without needing to simulate live SignalR events (disproportionate for this slice).
    expect(screen.getByText('Tailored resume')).toBeInTheDocument()
    expect(screen.getByText('Cover letter')).toBeInTheDocument()
  })

  it('shows an error banner when parsing fails', async () => {
    server.use(
      http.post('*/api/JobApplications/parse', () => new HttpResponse(null, { status: 500 })),
    )

    renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/^job posting$/i), 'Backend Engineer job posting text')
    await userEvent.click(screen.getByRole('button', { name: /parse posting/i }))

    expect(await screen.findByRole('alert')).toBeInTheDocument()
    // Provide-stage controls are interactive again after the failure.
    expect(screen.getByRole('button', { name: /parse posting/i })).toBeEnabled()
    expect(screen.getByLabelText(/^job posting$/i)).toBeEnabled()
  })
})
