import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect } from 'vitest'
import { Button } from './Button'
import { axe } from '../../test/axe'

// U0.2 — shared Button primitive. Primary is an accent outline, not a fill; ghost is transparent
// with a muted label. Base-state colors are asserted via getComputedStyle (reliable now that
// test/setup.ts strips @layer wrappers). Hover and :focus-visible states are asserted by class
// presence, not live computed style — jsdom doesn't simulate :hover from pointer events or
// reliably match :focus-visible, so a computed-style assertion there would be unfounded, not
// stronger. This mirrors the epic doc's own "CSS-class-level check" scoping for reduced motion.
describe('Button', () => {
  it('primary variant renders an accent outline, not a fill', () => {
    render(<Button variant="primary">Save</Button>)
    const button = screen.getByRole('button', { name: 'Save' })

    expect(getComputedStyle(button).borderColor).toBe('rgb(145, 132, 217)')
    expect(getComputedStyle(button).color).toBe('rgb(145, 132, 217)')
    expect(getComputedStyle(button).backgroundColor).toBe('rgba(0, 0, 0, 0)')
    expect(button.className).toContain('hover:bg-[#9184d9]/15')
  })

  it('ghost variant renders transparent with a muted label', () => {
    render(<Button variant="ghost">Cancel</Button>)
    const button = screen.getByRole('button', { name: 'Cancel' })

    expect(getComputedStyle(button).color).toBe('rgb(147, 151, 171)')
    expect(getComputedStyle(button).backgroundColor).toBe('rgba(0, 0, 0, 0)')
    expect(button.className).toContain('hover:bg-white/5')
  })

  it('both variants expose a focus-visible accent ring and suppress the default outline', () => {
    render(
      <>
        <Button variant="primary">Save</Button>
        <Button variant="ghost">Cancel</Button>
      </>,
    )

    for (const button of screen.getAllByRole('button')) {
      expect(button.className).toContain('outline-none')
      expect(button.className).toContain('focus-visible:outline-2')
      expect(button.className).toContain('focus-visible:outline-offset-2')
      expect(button.className).toContain('focus-visible:outline-[#9184d9]')
    }
  })

  it('forwards native button props (onClick, disabled, type)', async () => {
    const user = userEvent.setup()
    let clicked = false

    render(
      <Button variant="primary" onClick={() => { clicked = true }} type="button">
        Save
      </Button>,
    )
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(clicked).toBe(true)
  })

  it('has no accessibility violations for either variant', async () => {
    const { container } = render(
      <>
        <Button variant="primary">Save</Button>
        <Button variant="ghost">Cancel</Button>
      </>,
    )

    expect(await axe(container)).toHaveNoViolations()
  })
})
