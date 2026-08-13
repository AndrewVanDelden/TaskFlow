import { render, screen } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import type { AgentLog } from '../types'
import { IntakeProgress } from './IntakeProgress'

// IntakeProgress renders a <Link to="/board"> in its ready banner, so every render needs a router
// wrapper - matching this codebase's own convention for components that render router elements
// (see features/Navigation.test.tsx's MemoryRouter wrapping of App).
function renderIntakeProgress(logs: AgentLog[], resumeTaskId = 101, coverLetterTaskId = 102) {
  return render(
    <MemoryRouter>
      <IntakeProgress logs={logs} resumeTaskId={resumeTaskId} coverLetterTaskId={coverLetterTaskId} />
    </MemoryRouter>,
  )
}

const log = (taskId: number, action: string, details: string, createdAt: string): AgentLog => ({
  id: taskId * 100 + createdAt.length,
  taskId,
  agentName: 'ResumeTailoringAgent',
  action,
  details,
  success: true,
  createdAt,
})

describe('IntakeProgress', () => {
  it("shows each item's stage label", () => {
    const logs = [
      log(101, 'Claimed', 'claimed', '2026-08-12T10:00:00Z'),
      log(102, 'Claimed', 'claimed', '2026-08-12T10:00:00Z'),
      log(102, 'TailoredContentSaved', 'saved', '2026-08-12T10:05:00Z'),
    ]

    renderIntakeProgress(logs)

    expect(screen.getByText('Tailored resume')).toBeInTheDocument()
    expect(screen.getByText(/^in progress/i)).toBeInTheDocument()
    expect(screen.getByText('Cover letter')).toBeInTheDocument()
    expect(screen.getByText(/^saved, ready for review$/i)).toBeInTheDocument()
  })

  it('shows the ready banner and board link only when both items are saved', () => {
    const logs = [
      log(101, 'Claimed', 'claimed', '2026-08-12T10:00:00Z'),
      log(101, 'TailoredContentSaved', 'saved', '2026-08-12T10:05:00Z'),
      log(102, 'Claimed', 'claimed', '2026-08-12T10:00:00Z'),
      log(102, 'TailoredContentSaved', 'saved', '2026-08-12T10:05:00Z'),
    ]

    renderIntakeProgress(logs)

    expect(screen.getByText(/^ready for review/i)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /view it on the board/i })).toHaveAttribute('href', '/board')
  })

  it('does not show the ready banner when only one item is saved', () => {
    const logs = [
      log(101, 'Claimed', 'claimed', '2026-08-12T10:00:00Z'),
      log(101, 'TailoredContentSaved', 'saved', '2026-08-12T10:05:00Z'),
      log(102, 'Claimed', 'claimed', '2026-08-12T10:00:00Z'),
    ]

    renderIntakeProgress(logs)

    expect(screen.queryByText(/^ready for review/i)).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /view it on the board/i })).not.toBeInTheDocument()
  })
})
