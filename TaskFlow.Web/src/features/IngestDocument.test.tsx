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

describe('IngestDocument', () => {
  it('parses pasted text and shows the returned drafts', async () => {
    renderIngestDocument()

    await userEvent.type(screen.getByPlaceholderText('Paste a document'), '# doc')
    await userEvent.click(screen.getByRole('button', { name: /parse/i }))

    expect(await screen.findByText('Draft from server')).toBeInTheDocument()
  })

  it('approves the previewed drafts and reports how many were added', async () => {
    renderIngestDocument()

    await userEvent.type(screen.getByPlaceholderText('Paste a document'), '# doc')
    await userEvent.click(screen.getByRole('button', { name: /parse/i }))
    await screen.findByText('Draft from server')

    await userEvent.click(screen.getByRole('button', { name: /approve/i }))

    expect(await screen.findByText(/added 1 task to the board/i)).toBeInTheDocument()
  })

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
