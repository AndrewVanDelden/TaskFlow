import { render, screen } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import { AgentStatus } from './AgentStatus'
import type { AgentLog } from '../types'
import type { CycleEvent } from '../hooks/useAgentFeed'

const log: AgentLog = {
  id: 1, taskId: null, agentName: 'TaskPrioritizer', action: 'PrioritiesUpdated',
  details: null, success: true, createdAt: '2026-07-26T12:00:00Z',
}

describe('AgentStatus', () => {
  it('renders a card for each agent', () => {
    render(<AgentStatus logs={[]} cycles={{}} />)
    expect(screen.getByText('Task Prioritizer')).toBeInTheDocument()
    expect(screen.getByText('Stale Task Detector')).toBeInTheDocument()
  })

  it('shows Running for an agent whose cycle has started, Idle for the rest', () => {
    const cycles: Record<string, CycleEvent> = {
      TaskPrioritizer: { agentName: 'TaskPrioritizer', phase: 'started', at: '' },
    }
    render(<AgentStatus logs={[log]} cycles={cycles} />)
    expect(screen.getByText('Running')).toBeInTheDocument()
    expect(screen.getByText('Idle')).toBeInTheDocument()
  })
})
