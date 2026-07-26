import { render, screen } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import { AgentFeed } from './AgentFeed'
import type { AgentLog } from '../types'

const log: AgentLog = {
  id: 1, taskId: 5, agentName: 'StaleTaskDetector', action: 'Escalated',
  details: 'overdue 10 days', success: true, createdAt: '2026-07-26T12:00:00Z',
}

describe('AgentFeed', () => {
  it('renders a log action, agent name, and the Live indicator', () => {
    render(<AgentFeed logs={[log]} connected={true} />)
    expect(screen.getByText('Escalated')).toBeInTheDocument()
    expect(screen.getByText('StaleTaskDetector')).toBeInTheDocument()
    expect(screen.getByText('Live')).toBeInTheDocument()
  })

  it('shows the empty state and Offline when there are no logs', () => {
    render(<AgentFeed logs={[]} connected={false} />)
    expect(screen.getByText('No agent activity yet.')).toBeInTheDocument()
    expect(screen.getByText('Offline')).toBeInTheDocument()
  })
})
