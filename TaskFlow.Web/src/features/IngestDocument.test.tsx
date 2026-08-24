import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import { http, HttpResponse, delay } from 'msw'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { server } from '../test/server'
import { IngestDocument } from './IngestDocument'
import { axe } from '../test/axe'

// IngestDocument reads its ingestion session id from the :sessionId route param (App.tsx's
// /ingest/:sessionId route), so every render needs a routing wrapper that supplies one.
function renderIngestDocument() {
  return render(
    <MemoryRouter initialEntries={['/ingest/test-session-id']}>
      <Routes>
        <Route path="/ingest/:sessionId" element={<IngestDocument />} />
        <Route path="/board" element={<div>BOARD</div>} />
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
  // the user actually verify what the parser extracted (company and description), not just the
  // title, before they commit to starting tailoring against it.
  //
  // PR #57 review: this test previously also asserted `screen.getByText('Job Posting')` - the
  // default MSW handler's `section` fixture value - which only ever passed because IngestDocument
  // still rendered a `drafts[0].section` paragraph at the time. That render was dead code in
  // production (Section is always '' for this flow since Sprint 3/PR #55) and has been removed;
  // this test is reconciled to assert only what the real pipeline actually threads through
  // (company, description), not the now-unrendered section fixture value.
  it('the review-stage summary shows the parsed company and description, not just the title', async () => {
    renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/^job posting$/i), 'Backend Engineer job posting text')
    await userEvent.click(screen.getByRole('button', { name: /parse posting/i }))
    await screen.findByText(/job posting:\s*Backend Engineer/i)

    expect(screen.getByTestId('parsed-company')).toBeInTheDocument()
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

  // User report (2026-08-22): the base resume could only be pasted, unlike the job-posting and
  // generic-document inputs, which both already support an upload alongside the textarea.
  it('uploads a file and sets it as the base resume text', async () => {
    renderIngestDocument()

    const file = new File(['My uploaded resume content'], 'resume.txt', { type: 'text/plain' })

    await userEvent.upload(screen.getByLabelText(/or upload a resume file/i), file)

    expect(await screen.findByDisplayValue('My uploaded resume content')).toBeInTheDocument()
  })

  // User report (2026-08-22, second half): a PDF upload set the textarea to garbled raw binary
  // bytes (a plain UTF-8 decode of PDF bytes via File.text()) instead of readable text. PDFs are
  // now routed through POST /api/Files/extract-pdf-text (PdfPig, server-side) instead.
  it('uploads a PDF file and sets the extracted text as the base resume text', async () => {
    server.use(
      http.post('*/api/Files/extract-pdf-text', () => HttpResponse.json('Real extracted resume text')),
    )
    renderIngestDocument()

    const file = new File(['%PDF-1.4 fake bytes'], 'resume.pdf', { type: 'application/pdf' })

    await userEvent.upload(screen.getByLabelText(/or upload a resume file/i), file)

    expect(await screen.findByDisplayValue('Real extracted resume text')).toBeInTheDocument()
  })

  it('shows an error banner when base-resume PDF extraction fails', async () => {
    server.use(
      http.post('*/api/Files/extract-pdf-text', () =>
        HttpResponse.json({ message: 'The uploaded file is not a valid PDF.' }, { status: 400 })),
    )
    renderIngestDocument()

    const file = new File(['not really a pdf'], 'resume.pdf', { type: 'application/pdf' })

    await userEvent.upload(screen.getByLabelText(/or upload a resume file/i), file)

    expect(await screen.findByRole('alert')).toHaveTextContent('The uploaded file is not a valid PDF.')
  })

  // PR #68 review finding: a single shared error state disconnected the message from whichever
  // upload section actually failed. A generic-document upload failure must not leave the base-resume
  // section (or any other section) with a stray error, and vice versa - proven here by triggering a
  // failure in the generic-document section only.
  it('shows an error banner when the generic-document PDF extraction fails, independent of the other sections', async () => {
    server.use(
      http.post('*/api/Files/extract-pdf-text', () =>
        HttpResponse.json({ message: 'The uploaded file is not a valid PDF.' }, { status: 400 })),
    )
    renderIngestDocument()

    const file = new File(['not really a pdf'], 'document.pdf', { type: 'application/pdf' })

    await userEvent.upload(screen.getByLabelText(/or upload a document file/i), file)

    expect(await screen.findByRole('alert')).toHaveTextContent('The uploaded file is not a valid PDF.')
    expect(screen.getByLabelText(/^base resume$/i)).toHaveValue('')
  })

  // Epic 3.1 Sprint 4 (U4.4, engineer A's slice): startTailoring() now navigates to /board on
  // success (a real, intentional behavior change locked in the epic doc). Renamed and re-targeted
  // from its pre-Sprint-4 wording/assertion ("...moves to the building stage" / asserted on
  // IntakeProgress's in-place text): setStage('building') and navigate('/board') land in the same
  // React commit (confirmed empirically - no intermediate paint), so IngestDocument unmounts before
  // 'building' is ever visible here. The real, observable behavior is hand-off to the board.
  it('starting tailoring after parsing navigates to the board on success', async () => {
    renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/^job posting$/i), 'Backend Engineer job posting text')
    await userEvent.click(screen.getByRole('button', { name: /parse posting/i }))
    await screen.findByText(/job posting:\s*Backend Engineer/i)

    await userEvent.type(screen.getByLabelText(/base resume/i), 'My base resume text')
    await userEvent.click(screen.getByRole('button', { name: /start tailoring/i }))

    expect(await screen.findByText('BOARD')).toBeInTheDocument()
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
  // requirement.
  //
  // Sprint-4-closeout reconciliation (Epic 3.1 decision 2): this originally asserted
  // `/focus-visible:ring/` because the h1 used a hand-rolled `focus-visible:ring-2
  // focus-visible:ring-blue-500`. The closeout mapping table replaces that with the shared
  // `focusRingAccent` token, which uses an `outline` utility (`focus-visible:outline-2
  // focus-visible:outline-offset-2 focus-visible:outline-[#9184d9]`), not a `ring` utility - the
  // same mechanism `Login.tsx` and `Button.tsx` already use everywhere else in this app. The old
  // regex no longer matches anything the new className contains, so it's updated to look for the
  // `outline` mechanism instead (mirrors `Button.test.tsx`'s own class-presence checks for the
  // same focus-visible pattern).
  it('the stage heading keeps a visible focus ring when focused (T6.5)', () => {
    renderIngestDocument()

    expect(screen.getByRole('heading', { level: 1 }).className).toMatch(/focus-visible:outline/)
  })

  // T6.3's "renders live per-item progress rows once the building stage is reached" test (its own
  // dedicated test, pre-Sprint-4) is removed rather than reconciled: it asserted IntakeProgress's
  // rows render inside IngestDocument once stage reaches 'building', but Sprint 4's
  // navigate-on-success change (previous test above) means IngestDocument unmounts in the same
  // commit that stage flips to 'building', so that render was empirically unreachable through this
  // component's own tests. PR #57 review: this comment previously claimed the render branch was
  // "kept, not deleted" and that IntakeProgress.test.tsx still covered it - both false. This same
  // PR deletes IntakeProgress.tsx, IntakeProgress.test.tsx, and the 'building'-stage render branch
  // outright, confirmed dead code with zero remaining consumers (see the epic doc's Sprint 4
  // retrospective). Flagged in the Sprint 4 (U4.1-U4.5) report as a discovered consequence of
  // U4.4's navigation change, not invented scope.

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

describe('IngestDocument - URL input (Epic 3.2 S2.2/S2.3)', () => {
  it('renders a labeled job-posting URL input alongside the existing textarea at the provide stage', () => {
    renderIngestDocument()

    expect(screen.getByLabelText(/job posting url/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/^job posting$/i)).toBeInTheDocument()
  })

  it('parsing a URL shows the drafts and collapses the job-posting input to a summary', async () => {
    renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/job posting url/i), 'https://example.com/job-posting')
    await userEvent.click(screen.getByRole('button', { name: /^parse url$/i }))

    expect(await screen.findByText(/job posting:\s*Backend Engineer/i)).toBeInTheDocument()
    expect(screen.queryByLabelText(/job posting url/i)).not.toBeInTheDocument()
  })

  it('shows an error banner when URL parsing fails', async () => {
    server.use(
      http.post('*/api/JobApplications/parse-url', () => new HttpResponse(null, { status: 500 })),
    )

    renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/job posting url/i), 'https://example.com/job-posting')
    await userEvent.click(screen.getByRole('button', { name: /^parse url$/i }))

    expect(await screen.findByRole('alert')).toBeInTheDocument()
    // Provide-stage controls are interactive again after the failure.
    expect(screen.getByRole('button', { name: /^parse url$/i })).toBeEnabled()
    expect(screen.getByLabelText(/job posting url/i)).toBeEnabled()
  })

  // PR #64 review finding: the textarea signals its async state to assistive technology via
  // aria-busy (line 162); the URL input didn't. The response is delayed so the transient
  // 'parsing' stage stays observable long enough to assert on, matching the pattern the existing
  // 'starting stage' axe test already uses for the same reason.
  it('the URL input has aria-busy while parsing, matching the textarea', async () => {
    server.use(
      http.post('*/api/JobApplications/parse-url', async () => {
        await delay(50)
        return HttpResponse.json([
          { title: 'Backend Engineer', description: 'Build things.', kind: 'ResumeTailoring', section: 'Job Posting' },
        ])
      }),
    )

    renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/job posting url/i), 'https://example.com/job-posting')
    await userEvent.click(screen.getByRole('button', { name: /^parse url$/i }))

    expect(screen.getByLabelText(/job posting url/i)).toHaveAttribute('aria-busy', 'true')

    await screen.findByText(/job posting:\s*Backend Engineer/i)
  })
})

// U4.1 - 3-step indicator. 'provide'/'parsing' -> step 1, 'review'/'starting' -> step 2, 'building'
// -> step 3. The 'building' mapping is exercised only via lib/intakeSteps.test.ts's pure-function
// unit test, not end-to-end here: see the comment on the navigation test above for why 'building'
// never actually paints inside this component once startTailoring() succeeds.
describe('IngestDocument - 3-step indicator (U4.1)', () => {
  it('marks step 1 as the current step at the initial provide stage', () => {
    renderIngestDocument()

    expect(screen.getByText('1 Provide').closest('li')).toHaveAttribute('aria-current', 'step')
    expect(screen.getByText('2 Review').closest('li')).not.toHaveAttribute('aria-current')
    expect(screen.getByText('3 Generate').closest('li')).not.toHaveAttribute('aria-current')
  })

  it('marks step 2 as the current step once parsing succeeds and the review stage is reached', async () => {
    renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/^job posting$/i), 'Backend Engineer job posting text')
    await userEvent.click(screen.getByRole('button', { name: /parse posting/i }))
    await screen.findByText(/job posting:\s*Backend Engineer/i)

    expect(screen.getByText('1 Provide').closest('li')).not.toHaveAttribute('aria-current')
    expect(screen.getByText('2 Review').closest('li')).toHaveAttribute('aria-current', 'step')
    expect(screen.getByText('3 Generate').closest('li')).not.toHaveAttribute('aria-current')
  })
})

// U4.2 - parsed-result card: company, present and absent. The default MSW parse handler
// (src/test/handlers.ts) returns no `company` field at all, so the "absent" case is the existing
// default and the "present" case needs a server.use(...) override - checked directly, not assumed.
describe('IngestDocument - parsed-result card company (U4.2)', () => {
  it('shows the parsed company when present', async () => {
    server.use(
      http.post('*/api/JobApplications/parse', () =>
        HttpResponse.json([
          { title: 'Backend Engineer', description: 'Build things.', kind: 'ResumeTailoring', section: 'Job Posting', company: 'Acme Corp' },
        ])),
    )

    renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/^job posting$/i), 'Backend Engineer job posting text')
    await userEvent.click(screen.getByRole('button', { name: /parse posting/i }))
    await screen.findByText(/job posting:\s*Backend Engineer/i)

    expect(screen.getByTestId('parsed-company')).toHaveTextContent('Acme Corp')
  })

  it('shows the em-dash quiet placeholder when the parsed company is absent (default MSW handler)', async () => {
    renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/^job posting$/i), 'Backend Engineer job posting text')
    await userEvent.click(screen.getByRole('button', { name: /parse posting/i }))
    await screen.findByText(/job posting:\s*Backend Engineer/i)

    expect(screen.getByTestId('parsed-company')).toHaveTextContent('—')
  })
})

// U4.3 - click-to-expand base resume preview. Additive sibling next to the existing textarea, which
// stays exactly as-is (asserted throughout the rest of this file via getByLabelText(/base resume/i)).
describe('IngestDocument - click-to-expand base resume preview (U4.3)', () => {
  it('does not render the preview disclosure while the base resume is empty', () => {
    renderIngestDocument()

    expect(screen.queryByText(/preview base resume/i)).not.toBeInTheDocument()
  })

  it('does not render the preview disclosure while the base resume is whitespace-only', async () => {
    renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/base resume/i), '   ')

    expect(screen.queryByText(/preview base resume/i)).not.toBeInTheDocument()
  })

  it('starts collapsed and expands on click to render the resume through MarkdownPreview', async () => {
    renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/base resume/i), '# My Resume{enter}Some content')

    const summary = screen.getByText(/preview base resume/i)
    const details = summary.closest('details')
    expect(details).not.toBeNull()
    expect(details).not.toHaveAttribute('open')

    await userEvent.click(summary)

    expect(details).toHaveAttribute('open')
    expect(await screen.findByRole('heading', { name: 'My Resume' })).toBeInTheDocument()
  })

  // Native <summary> is keyboard-operable in real browsers by construction (Enter/Space activate
  // it, same as a button) - no extra ARIA needed. Verified directly that this jsdom version (29.x)
  // does not simulate that Enter/Space -> activation translation for <summary> the way real
  // browsers do (a scratch check confirmed: focusing the summary and dispatching {Enter}/{space}
  // via userEvent.keyboard left the details closed) - a jsdom gap, not a component behavior to work
  // around. Per this slice's brief, a click on the (keyboard-focusable) summary is an accepted
  // stand-in proof of operability in this environment.
  it('the preview summary is focusable and keyboard-operable (native <summary>/<details> semantics)', async () => {
    renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/base resume/i), 'Plain text resume')

    const summary = screen.getByText(/preview base resume/i)
    const details = summary.closest('details')

    summary.focus()
    expect(summary).toHaveFocus()

    await userEvent.click(summary)

    expect(details).toHaveAttribute('open')
  })
})

// U4.5 - accessibility pass. 'building' is substituted with 'starting' as the third stage: it is
// the last stage actually observable in a mounted IngestDocument before startTailoring()'s success
// path navigates away (see the navigation test's comment above) - 'building' itself is never
// painted here, so there is nothing for axe to inspect at that stage through this component. The
// resume-context save is delayed so the 'starting' stage stays on screen long enough for axe to run
// before the mocked assemble call resolves and navigation fires.
// Sprint-4-closeout restyle (Epic 3.1 decision 2): the audit found this file's pre-existing
// majority (both textareas, their buttons/labels/file inputs, and the generic-document flow) was
// left in old pre-Nocturne Tailwind styling, including 5 leftover blue focus rings, while only the
// new U4.1-U4.3 pieces were restyled in Sprint 4. This block asserts the specific class-level
// changes from the epic doc's locked mapping table (the "Exact mapping" table under "Decisions
// (2026-08-16)") - the only things a test can actually observe about a pure restyle: className
// content, computed color (mirrors Button.test.tsx's own convention for verifying the shared
// Button primitive is in use), and focus-ring mechanism.
describe('IngestDocument - Nocturne restyle close-out (Sprint 4 audit fix)', () => {
  it('the job-posting textarea has the Nocturne surface/border/text/focus-ring classes, no old slate-900/slate-700', () => {
    renderIngestDocument()

    const textarea = screen.getByLabelText(/^job posting$/i)
    expect(textarea.className).toContain('bg-[#232532]')
    expect(textarea.className).toContain('border-white/10')
    expect(textarea.className).toContain('text-white')
    expect(textarea.className).toMatch(/focus-visible:outline/)
    expect(textarea.className).not.toMatch(/bg-slate-900|border-slate-700/)
  })

  it('the base-resume textarea has the Nocturne surface/border/text/focus-ring classes, no old slate-900/slate-700', () => {
    renderIngestDocument()

    const textarea = screen.getByLabelText(/base resume/i)
    expect(textarea.className).toContain('bg-[#232532]')
    expect(textarea.className).toContain('border-white/10')
    expect(textarea.className).toContain('text-white')
    expect(textarea.className).toMatch(/focus-visible:outline/)
    expect(textarea.className).not.toMatch(/bg-slate-900|border-slate-700/)
  })

  it('the generic-document textarea has the Nocturne surface/border/text/focus-ring classes, no old slate-900/slate-700', async () => {
    renderIngestDocument()
    await expandGenericFlow()

    const textarea = screen.getByLabelText(/paste a document/i)
    expect(textarea.className).toContain('bg-[#232532]')
    expect(textarea.className).toContain('border-white/10')
    expect(textarea.className).toContain('text-white')
    expect(textarea.className).toMatch(/focus-visible:outline/)
    expect(textarea.className).not.toMatch(/bg-slate-900|border-slate-700/)
  })

  it('both file inputs use the Nocturne token classes, not the old blue focus ring', async () => {
    renderIngestDocument()
    await expandGenericFlow()

    const jobPostingFile = screen.getByLabelText(/or upload a file/i)
    const genericFile = screen.getByLabelText(/upload a document file/i)

    for (const input of [jobPostingFile, genericFile]) {
      expect(input.className).toContain('file:border-[rgba(233,233,237,0.16)]')
      expect(input.className).toContain('file:bg-[#232532]')
      expect(input.className).toMatch(/focus-visible:outline/)
      expect(input.className).not.toMatch(/ring-blue-500/)
    }
  })

  it('the stage heading no longer carries the old blue focus-ring class', () => {
    renderIngestDocument()

    expect(screen.getByRole('heading', { level: 1 }).className).not.toMatch(/ring-blue-500/)
  })

  // Verifies the four hand-rolled buttons now render through the shared Button primitive
  // (variant="primary": an accent outline, not a color fill) rather than bg-blue-600/bg-emerald-600
  // - same computed-style convention Button.test.tsx uses to verify its own primary variant.
  it('the "Parse posting" button renders as the shared Button primitive, not a hand-rolled blue button', () => {
    renderIngestDocument()

    const button = screen.getByRole('button', { name: /parse posting/i })
    expect(getComputedStyle(button).borderColor).toBe('rgb(145, 132, 217)')
    expect(getComputedStyle(button).backgroundColor).toBe('rgba(0, 0, 0, 0)')
    expect(button.className).not.toMatch(/bg-blue-600/)
  })

  it('the "Save base resume" button renders as the shared Button primitive, not a hand-rolled blue button', () => {
    renderIngestDocument()

    const button = screen.getByRole('button', { name: /save base resume/i })
    expect(getComputedStyle(button).borderColor).toBe('rgb(145, 132, 217)')
    expect(getComputedStyle(button).backgroundColor).toBe('rgba(0, 0, 0, 0)')
    expect(button.className).not.toMatch(/bg-blue-600/)
  })

  it('the generic-document "Parse" button renders as the shared Button primitive, not a hand-rolled blue button', async () => {
    renderIngestDocument()
    await expandGenericFlow()

    const button = screen.getByRole('button', { name: /^parse$/i })
    expect(getComputedStyle(button).borderColor).toBe('rgb(145, 132, 217)')
    expect(getComputedStyle(button).backgroundColor).toBe('rgba(0, 0, 0, 0)')
    expect(button.className).not.toMatch(/bg-blue-600/)
  })

  it('the "Approve and add to board" button renders as the shared Button primitive, not a hand-rolled emerald button', async () => {
    renderIngestDocument()
    await expandGenericFlow()

    await userEvent.type(screen.getByPlaceholderText('Paste a document'), '# doc')
    await userEvent.click(screen.getByRole('button', { name: /^parse$/i }))
    await screen.findByText('Draft from server')

    const button = screen.getByRole('button', { name: /approve/i })
    expect(getComputedStyle(button).borderColor).toBe('rgb(145, 132, 217)')
    expect(getComputedStyle(button).backgroundColor).toBe('rgba(0, 0, 0, 0)')
    expect(button.className).not.toMatch(/bg-emerald-600/)
  })

  it('the "Use previously saved base resume" link uses accent tokens, not the old blue link classes, and stays a plain button (not the shared Button component)', async () => {
    server.use(
      http.get('*/api/JobApplications/resume-context/latest', () =>
        HttpResponse.json({ content: 'Reusable resume text', contentFormat: 'text', updatedAt: '2026-08-01T00:00:00Z' })),
    )

    renderIngestDocument()

    const reuseButton = await screen.findByRole('button', { name: /use previously saved base resume/i })
    expect(reuseButton.className).toContain('text-[#9184d9]')
    expect(reuseButton.className).toContain('hover:text-[#e7e5fe]')
    expect(reuseButton.className).toMatch(/focus-visible:outline/)
    expect(reuseButton.className).not.toMatch(/text-blue-400|ring-blue-500/)
    // Not the shared Button primitive: its variants carry padding/border treatments wrong for an
    // inline text link (locked decision, epic doc mapping table).
    expect(getComputedStyle(reuseButton).borderStyle).not.toBe('solid')
  })

  it('the job-posting-parse error banner matches Login.tsx\'s red shade, not the old mismatched red', async () => {
    server.use(
      http.post('*/api/JobApplications/parse', () => new HttpResponse(null, { status: 500 })),
    )

    renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/^job posting$/i), 'Backend Engineer job posting text')
    await userEvent.click(screen.getByRole('button', { name: /parse posting/i }))

    const alert = await screen.findByRole('alert')
    expect(alert.className).toContain('text-red-300')
    expect(alert.className).toContain('bg-red-500/10')
    expect(alert.className).toContain('border-red-500/30')
    expect(alert.className).not.toMatch(/text-red-400|bg-red-950|border-red-900/)
  })

  it('the generic-document draft list item uses Nocturne divider/surface tokens, and its section meta uses textNeutral500', async () => {
    renderIngestDocument()
    await expandGenericFlow()

    await userEvent.type(screen.getByPlaceholderText('Paste a document'), '# doc')
    await userEvent.click(screen.getByRole('button', { name: /^parse$/i }))
    const title = await screen.findByText('Draft from server')

    const item = title.closest('li')
    expect(item).not.toBeNull()
    expect(item!.className).toContain('border-[rgba(233,233,237,0.16)]')
    expect(item!.className).toContain('bg-[#232532]')
    expect(item!.className).not.toMatch(/border-slate-800|bg-slate-900/)

    const meta = screen.getByText('Doc')
    expect(meta.className).toContain('text-[#9397ab]')
    expect(meta.className).not.toMatch(/text-slate-500/)
  })
})

describe('IngestDocument - accessibility (U4.5)', () => {
  it('has no accessibility violations at the provide stage', async () => {
    const { container } = renderIngestDocument()

    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no accessibility violations at the review stage', async () => {
    const { container } = renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/^job posting$/i), 'Backend Engineer job posting text')
    await userEvent.click(screen.getByRole('button', { name: /parse posting/i }))
    await screen.findByText(/job posting:\s*Backend Engineer/i)

    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no accessibility violations at the starting stage', async () => {
    server.use(
      http.post('*/api/JobApplications/resume-context', async () => {
        await delay(50)
        return HttpResponse.json(true)
      }),
    )

    const { container } = renderIngestDocument()

    await userEvent.type(screen.getByLabelText(/^job posting$/i), 'Backend Engineer job posting text')
    await userEvent.click(screen.getByRole('button', { name: /parse posting/i }))
    await screen.findByText(/job posting:\s*Backend Engineer/i)

    await userEvent.type(screen.getByLabelText(/base resume/i), 'My base resume text')
    await userEvent.click(screen.getByRole('button', { name: /start tailoring/i }))
    // Synchronous, not findBy: setStage('starting') happens before the first `await` inside
    // startTailoring(), so it's already flushed by the time userEvent.click's act() wrapper
    // returns. A findBy's polling window risks overshooting past the delayed resume-context
    // response into the 'building' -> navigate transition.
    expect(screen.getByRole('button', { name: /tailoring…/i })).toBeInTheDocument()

    expect(await axe(container)).toHaveNoViolations()
  })
})
