import { render } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import { designTokens } from './tokens'

// Corrected token table, Epic 3.1 Sprint 0 (U0.1). Page bg, surface, text, and the full
// accent/neutral ramps from TaskFlow_Epic3.1_UIRevamp.md's "Confirmed against the repo" table,
// asserted via getComputedStyle rather than eyeballed. Divider/border is intentionally not
// asserted here — jsdom does not reliably reflect border-color in computed style when no
// border-style/width is set, so that token is defined in tokens.ts but verified visually by
// the components that actually render a border (Sprint 1+), not by this test.
describe('Nocturne design tokens', () => {
  it.each(designTokens)('$name ($className) resolves $property to $expected', ({ className, property, expected }) => {
    const { container } = render(<div className={className} />)
    const el = container.firstElementChild as HTMLElement

    expect(getComputedStyle(el)[property]).toBe(expected)
  })
})
