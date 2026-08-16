import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import { TailorButton } from './TailorButton'
import { mockPrefersReducedMotion } from '../test/reducedMotion'
import { axe } from '../test/axe'

describe('TailorButton', () => {
  it('renders "Start tailoring" with an accessible name when idle', () => {
    render(<TailorButton onClick={vi.fn()} disabled={false} busy={false} />)
    expect(screen.getByRole('button', { name: /start tailoring/i })).toBeInTheDocument()
  })

  it('calls onClick exactly once when clicked and not disabled', async () => {
    const onClick = vi.fn()
    render(<TailorButton onClick={onClick} disabled={false} busy={false} />)
    await userEvent.click(screen.getByRole('button', { name: /start tailoring/i }))
    expect(onClick).toHaveBeenCalledOnce()
  })

  it('does not call onClick when disabled, and reports disabled', async () => {
    const onClick = vi.fn()
    render(<TailorButton onClick={onClick} disabled={true} busy={false} />)
    const button = screen.getByRole('button')
    expect(button).toBeDisabled()
    await userEvent.click(button)
    expect(onClick).not.toHaveBeenCalled()
  })

  // disabled must win over busy's styling - native disabled attribute blocks the click regardless
  // of whether busy is also true (this codebase's own reminder: check the disabled+busy combo).
  it('does not call onClick when both disabled and busy are true', async () => {
    const onClick = vi.fn()
    render(<TailorButton onClick={onClick} disabled={true} busy={true} />)
    const button = screen.getByRole('button')
    expect(button).toBeDisabled()
    await userEvent.click(button)
    expect(onClick).not.toHaveBeenCalled()
  })

  it('shows the "Tailoring…" label and the glow/spark animation classes when busy and motion is not reduced', () => {
    mockPrefersReducedMotion(false)
    render(<TailorButton onClick={vi.fn()} disabled={false} busy={true} />)

    expect(screen.getByText('Tailoring…')).toBeInTheDocument()
    expect(document.querySelector('[class*="animate-\\[goGlow"]')).not.toBeNull()

    const sparks = screen.getAllByTestId('tailor-spark')
    expect(sparks).toHaveLength(8)
    sparks.forEach((spark) => {
      expect(spark.className).toMatch(/animate-\[sparkFly/)
    })
  })

  it('shows the "Tailoring…" label but no animation classes when busy and motion is reduced', () => {
    mockPrefersReducedMotion(true)
    render(<TailorButton onClick={vi.fn()} disabled={false} busy={true} />)

    expect(screen.getByText('Tailoring…')).toBeInTheDocument()
    expect(document.querySelector('[class*="animate-\\[goGlow"]')).toBeNull()
    expect(document.querySelector('[class*="animate-\\[sparkFly"]')).toBeNull()
    expect(document.querySelector('.animate-spin')).toBeNull()
    expect(screen.queryAllByTestId('tailor-spark')).toHaveLength(0)
  })

  // Regardless of the reduced-motion setting, no animation classes should render when not busy.
  it('renders no animation classes when idle, even with motion not reduced', () => {
    mockPrefersReducedMotion(false)
    render(<TailorButton onClick={vi.fn()} disabled={false} busy={false} />)

    expect(document.querySelector('[class*="animate-\\[goGlow"]')).toBeNull()
    expect(document.querySelector('[class*="animate-\\[sparkFly"]')).toBeNull()
    expect(document.querySelector('.animate-spin')).toBeNull()
  })

  it('has no accessibility violations when idle', async () => {
    const { container } = render(<TailorButton onClick={vi.fn()} disabled={false} busy={false} />)
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no accessibility violations when busy', async () => {
    mockPrefersReducedMotion(false)
    const { container } = render(<TailorButton onClick={vi.fn()} disabled={false} busy={true} />)
    expect(await axe(container)).toHaveNoViolations()
  })
})
