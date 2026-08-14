import { render, screen } from '@testing-library/react'
import { describe, it, expect, vi } from 'vitest'
import { Dashboard } from './Dashboard'
import type { AgentLog } from '../types'
import { axe } from '../test/axe'

const logs: AgentLog[] = [
  {
    id: 1, taskId: 5, agentName: 'StaleTaskDetector', action: 'Escalated',
    details: 'overdue 10 days', success: true, createdAt: '2026-07-26T12:00:00Z',
  },
]

vi.mock('../hooks/useAgentFeed', () => ({
  useAgentFeed: () => ({ logs, cycles: {}, connected: true }),
}))

describe('Dashboard', () => {
  it('renders the Activity heading', () => {
    render(<Dashboard />)

    expect(screen.getByRole('heading', { name: 'Activity' })).toBeInTheDocument()
  })

  it('passes the feed data through to the shared AgentFeedList', () => {
    render(<Dashboard />)

    expect(screen.getByText('StaleTaskDetector')).toBeInTheDocument()
    expect(screen.getByText('overdue 10 days')).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    const { container } = render(<Dashboard />)

    expect(await axe(container)).toHaveNoViolations()
  })
})
