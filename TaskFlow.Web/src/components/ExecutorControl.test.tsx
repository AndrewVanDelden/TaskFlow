import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect } from 'vitest'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import { ExecutorControl } from './ExecutorControl'

describe('ExecutorControl', () => {
  it('shows the current state and enables a paused executor', async () => {
    server.use(
      http.get('*/api/agents/executor', () => HttpResponse.json({ enabled: false })),
      http.post('*/api/agents/executor/enable', () => HttpResponse.json({ enabled: true })),
    )

    render(<ExecutorControl />)

    // Seeded paused: the button offers to enable.
    const enableBtn = await screen.findByRole('button', { name: 'Enable' })
    await userEvent.click(enableBtn)

    // Now enabled: the button offers to pause.
    await waitFor(() => expect(screen.getByRole('button', { name: 'Pause' })).toBeInTheDocument())
  })
})
