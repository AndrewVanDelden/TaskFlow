import { render, screen } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import { AgentFeedList } from './AgentFeedList'
import type { AgentLog } from '../types'
import { axe } from '../test/axe'

const log: AgentLog = {
  id: 1, taskId: 5, agentName: 'StaleTaskDetector', action: 'Escalated',
  details: 'overdue 10 days', success: true, createdAt: '2026-07-26T12:00:00Z',
}

describe('AgentFeedList', () => {
  it('renders the agent name and details for each log', () => {
    render(<AgentFeedList logs={[log]} />)

    expect(screen.getByText('StaleTaskDetector')).toBeInTheDocument()
    expect(screen.getByText('overdue 10 days')).toBeInTheDocument()
  })

  it('falls back to the action when details is null', () => {
    render(<AgentFeedList logs={[{ ...log, details: null }]} />)

    expect(screen.getByText('Escalated')).toBeInTheDocument()
  })

  it('falls back to the action when details is an empty string', () => {
    render(<AgentFeedList logs={[{ ...log, details: '' }]} />)

    expect(screen.getByText('Escalated')).toBeInTheDocument()
  })

  it('renders a relative-time string for each log', () => {
    render(<AgentFeedList logs={[log]} />)

    expect(screen.getByRole('listitem').textContent).toMatch(/ago|just now/)
  })

  it('shows the empty state when there are no logs', () => {
    render(<AgentFeedList logs={[]} />)

    expect(screen.getByText('No agent activity yet.')).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    const { container } = render(<AgentFeedList logs={[log]} />)

    expect(await axe(container)).toHaveNoViolations()
  })
})
