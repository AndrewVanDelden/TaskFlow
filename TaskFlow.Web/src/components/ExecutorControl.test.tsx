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

  // Board screenshot feedback (2026-08-14): the accent-purple/near-invisible-white dot pairing was
  // too hard to tell apart at a glance. Matches this same screen's own existing running/idle
  // vocabulary instead of inventing a third one - AgentStatus's "Running" pill and Dashboard's
  // "Live" connection dot both already use emerald for "on"; neither uses red for "off", so paused
  // matches AgentStatus's "Idle" dot (a solid, clearly-visible neutral) rather than introducing red
  // where no adjacent precedent for it exists on this screen.
  it('shows a solid emerald dot when running', async () => {
    server.use(http.get('*/api/agents/executor', () => HttpResponse.json({ enabled: true })))

    render(<ExecutorControl />)

    await screen.findByRole('button', { name: 'Pause' })
    expect(screen.getByTestId('executor-status-dot').className).toContain('emerald')
  })

  it('shows a solid, clearly-visible neutral dot (not emerald, not near-invisible) when paused', async () => {
    server.use(http.get('*/api/agents/executor', () => HttpResponse.json({ enabled: false })))

    render(<ExecutorControl />)

    await screen.findByRole('button', { name: 'Enable' })
    const dotClass = screen.getByTestId('executor-status-dot').className
    expect(dotClass).not.toContain('emerald')
    expect(dotClass).not.toContain('white/20')
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

  // Board screenshot feedback (2026-08-14): this row renders directly in Dashboard's full-width
  // <main>, above the two-column split - the old bare flex-1 row stretched across the entire page,
  // leaving "Pause"/"Enable" stranded far from the status text it belongs with. Capping the row's
  // own width keeps the control a compact, self-contained group regardless of the page's width.
  it('keeps the status text and button in a width-capped group, not stretched full-page', async () => {
    server.use(http.get('*/api/agents/executor', () => HttpResponse.json({ enabled: true })))

    render(<ExecutorControl />)

    await screen.findByRole('button', { name: 'Pause' })
    expect(screen.getByTestId('executor-control-row').className).toMatch(/max-w-/)
  })

  it('has no accessibility violations', async () => {
    server.use(http.get('*/api/agents/executor', () => HttpResponse.json({ enabled: true })))

    const { container } = render(<ExecutorControl />)
    await screen.findByRole('button', { name: 'Pause' })

    expect(await axe(container)).toHaveNoViolations()
  })
})
