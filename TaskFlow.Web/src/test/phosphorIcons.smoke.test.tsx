import { render, screen } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import { KanbanIcon } from '@phosphor-icons/react'

// U0.6 — proves @phosphor-icons/react renders and follows the decorative-by-default contract:
// the icon itself is aria-hidden, the interactive control it sits inside carries the accessible name.
describe('@phosphor-icons/react smoke test', () => {
  it('renders an icon without throwing, decorative via explicit aria-hidden', () => {
    const { container } = render(<KanbanIcon aria-hidden="true" />)
    const svg = container.querySelector('svg')

    expect(svg).not.toBeNull()
    expect(svg?.getAttribute('aria-hidden')).toBe('true')
  })

  it('exposes its accessible name via the surrounding control, not the icon', () => {
    render(
      <button aria-label="Board">
        <KanbanIcon aria-hidden="true" />
      </button>,
    )

    expect(screen.getByRole('button', { name: 'Board' })).toBeDefined()
  })
})
