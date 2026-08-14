import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect } from 'vitest'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import { ExecutorControl } from './ExecutorControl'
import { mockPrefersReducedMotion } from '../test/reducedMotion'
import { axe } from '../test/axe'

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

  it('pulses the status dot when running and motion is not reduced', async () => {
    mockPrefersReducedMotion(false)
    server.use(http.get('*/api/agents/executor', () => HttpResponse.json({ enabled: true })))

    render(<ExecutorControl />)

    await screen.findByRole('button', { name: 'Pause' })
    expect(screen.getByTestId('executor-status-dot').className).toContain('animate-pulse')
  })

  it('does not pulse the status dot when motion is reduced, even when running', async () => {
    mockPrefersReducedMotion(true)
    server.use(http.get('*/api/agents/executor', () => HttpResponse.json({ enabled: true })))

    render(<ExecutorControl />)

    await screen.findByRole('button', { name: 'Pause' })
    expect(screen.getByTestId('executor-status-dot').className).not.toContain('animate-pulse')
  })

  it('shows "Executor running" when enabled', async () => {
    server.use(http.get('*/api/agents/executor', () => HttpResponse.json({ enabled: true })))

    render(<ExecutorControl />)

    expect(await screen.findByText('Executor running')).toBeInTheDocument()
  })

  it('shows "Executor paused" when disabled', async () => {
    server.use(http.get('*/api/agents/executor', () => HttpResponse.json({ enabled: false })))

    render(<ExecutorControl />)

    expect(await screen.findByText('Executor paused')).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    server.use(http.get('*/api/agents/executor', () => HttpResponse.json({ enabled: true })))

    const { container } = render(<ExecutorControl />)
    await screen.findByRole('button', { name: 'Pause' })

    expect(await axe(container)).toHaveNoViolations()
  })
})
