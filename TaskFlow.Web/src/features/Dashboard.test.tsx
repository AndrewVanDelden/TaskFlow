import { render, screen } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { Dashboard } from './Dashboard'
import type { AgentLog } from '../types'
import { axe } from '../test/axe'
import { useAgentFeed } from '../hooks/useAgentFeed'

const logs: AgentLog[] = [
  {
    id: 1, taskId: 5, agentName: 'StaleTaskDetector', action: 'Escalated',
    details: 'overdue 10 days', success: true, createdAt: '2026-07-26T12:00:00Z',
  },
]

vi.mock('../hooks/useAgentFeed')

describe('Dashboard', () => {
  beforeEach(() => {
    vi.mocked(useAgentFeed).mockReturnValue({ logs, cycles: {}, connected: true })
  })

  it('renders the Activity heading', () => {
    render(<Dashboard />)

    expect(screen.getByRole('heading', { name: 'Activity' })).toBeInTheDocument()
  })

  it('passes the feed data through to the shared AgentFeedList', () => {
    render(<Dashboard />)

    expect(screen.getByText('StaleTaskDetector')).toBeInTheDocument()
    expect(screen.getByText('overdue 10 days')).toBeInTheDocument()
  })

  // PR #55 review (finding 2, PLAUSIBLE): the Board's Live/Offline SignalR-connection indicator
  // was silently dropped when Dashboard swapped AgentFeed for the shared AgentFeedList, which has
  // no equivalent. Restored as a small indicator next to the Activity heading.
  it('shows a Live indicator when the agent hub is connected', () => {
    render(<Dashboard />)

    expect(screen.getByText('Live')).toBeInTheDocument()
  })

  it('shows an Offline indicator when the agent hub is not connected', () => {
    vi.mocked(useAgentFeed).mockReturnValue({ logs, cycles: {}, connected: false })

    render(<Dashboard />)

    expect(screen.getByText('Offline')).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    const { container } = render(<Dashboard />)

    expect(await axe(container)).toHaveNoViolations()
  })

  // PR #61 review finding: the Offline dot used bg-slate-600, mismatched with the identical
  // "inactive" dot shade (bg-slate-500) ExecutorControl.tsx and AgentStatus.tsx already use.
  it('shows the connection dot in the same inactive slate-500 shade as ExecutorControl/AgentStatus when offline', () => {
    vi.mocked(useAgentFeed).mockReturnValue({ logs, cycles: {}, connected: false })

    render(<Dashboard />)

    // Tailwind v4's default palette colors (slate-500/600) resolve through a CSS custom property
    // that jsdom doesn't fully resolve in getComputedStyle, so this asserts via className, the
    // same convention ExecutorControl.test.tsx already uses for its own slate/emerald status dot.
    const dot = screen.getByText('Offline').firstElementChild as HTMLElement
    expect(dot.className).toContain('bg-slate-500')
    expect(dot.className).not.toContain('bg-slate-600')
  })
})
