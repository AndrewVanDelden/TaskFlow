import { render } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import { axe } from './axe'

// U0.4 — proves vitest-axe is actually wired, not vacuously passing. Uses a missing-alt-text
// violation rather than the epic doc's "contrast violation": axe-core's color-contrast rule
// needs real layout/paint geometry jsdom doesn't provide, so it's unreliable there. Missing
// alt-text is a structural DOM check axe-core evaluates without real rendering.
describe('vitest-axe wiring', () => {
  it('flags a real accessibility violation (image missing alt text)', async () => {
    const { container } = render(<img src="x.png" />)

    const results = await axe(container)

    expect(results.violations.length).toBeGreaterThan(0)
  })

  it('reports no violations for an accessible image, proving toHaveNoViolations is wired', async () => {
    const { container } = render(<img src="x.png" alt="A descriptive label" />)

    const results = await axe(container)

    expect(results).toHaveNoViolations()
  })
})
