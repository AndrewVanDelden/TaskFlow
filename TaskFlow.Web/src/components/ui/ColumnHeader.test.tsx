import { render, screen } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import { ColumnHeader } from './ColumnHeader'
import { axe } from '../../test/axe'

// U0.3 — shared ColumnHeader primitive. Both label and count must render in neutral-500, not
// neutral-600 — the design handoff's cheat sheet had the count in neutral-600, but the epic doc's
// own contrast calculation found neutral-600 (#75798c, 4.09:1) fails WCAG AA while neutral-500
// (#9397ab, 6.1:1) passes, so this asserts the corrected color, not the original spec.
describe('ColumnHeader', () => {
  it('renders the label and count as text', () => {
    render(<ColumnHeader label="To Do" count={12} />)

    expect(screen.getByText('To Do')).toBeInTheDocument()
    expect(screen.getByText('12')).toBeInTheDocument()
  })

  it('renders the label in neutral-500, not neutral-600', () => {
    render(<ColumnHeader label="To Do" count={12} />)
    const label = screen.getByText('To Do')

    expect(getComputedStyle(label).color).toBe('rgb(147, 151, 171)')
  })

  it('renders the count in neutral-500, not neutral-600', () => {
    render(<ColumnHeader label="To Do" count={12} />)
    const count = screen.getByText('12')

    expect(getComputedStyle(count).color).toBe('rgb(147, 151, 171)')
  })

  it('renders the label as a heading', () => {
    render(<ColumnHeader label="To Do" count={12} />)

    expect(screen.getByRole('heading', { name: 'To Do' })).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    const { container } = render(<ColumnHeader label="To Do" count={12} />)

    expect(await axe(container)).toHaveNoViolations()
  })
})
