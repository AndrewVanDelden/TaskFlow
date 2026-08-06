import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect } from 'vitest'
import { IngestDocument } from './IngestDocument'

describe('IngestDocument', () => {
  it('parses pasted text and shows the returned drafts', async () => {
    render(<IngestDocument />)

    await userEvent.type(screen.getByPlaceholderText('Paste a document'), '# doc')
    await userEvent.click(screen.getByRole('button', { name: /parse/i }))

    expect(await screen.findByText('Draft from server')).toBeInTheDocument()
  })

  it('approves the previewed drafts and reports how many were added', async () => {
    render(<IngestDocument />)

    await userEvent.type(screen.getByPlaceholderText('Paste a document'), '# doc')
    await userEvent.click(screen.getByRole('button', { name: /parse/i }))
    await screen.findByText('Draft from server')

    await userEvent.click(screen.getByRole('button', { name: /approve/i }))

    expect(await screen.findByText(/added 1 task to the board/i)).toBeInTheDocument()
  })
})
