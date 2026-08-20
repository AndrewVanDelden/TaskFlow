import { render, screen } from '@testing-library/react'
import { describe, it, expect, vi } from 'vitest'
import { ReviewActions } from './ReviewActions'
import { axe } from '../test/axe'

// PR #61 review finding: the rejection-reason textarea was missed by the Nocturne restyle and
// still carried stock pre-Nocturne Tailwind (bg-slate-900/border-slate-700, no focus ring at all).
// Matches the locked text-input pattern (Login.tsx's inputClass / IngestDocument.tsx's textareas):
// bgSurface + border-white/10 + focusRingAccent. The Approve/Reject buttons stay hand-rolled
// green/red per the epic's close-out audit exception and are asserted here only to prove they are
// untouched by this fix.
describe('ReviewActions', () => {
  it('renders the reason textarea with the Nocturne surface/border/focus-ring classes, no old slate-900/slate-700', () => {
    render(<ReviewActions onApprove={vi.fn()} onReject={vi.fn()} />)

    const textarea = screen.getByPlaceholderText(/reason for rejection/i)
    expect(getComputedStyle(textarea).backgroundColor).toBe('rgb(35, 37, 50)')
    expect(textarea.className).toContain('border-white/10')
    expect(textarea.className).toContain('text-white')
    expect(textarea.className).toContain('placeholder-[#9397ab]')
    expect(textarea.className).toMatch(/focus-visible:outline/)
    expect(textarea.className).not.toMatch(/bg-slate-900|border-slate-700|placeholder-slate-500/)
  })

  it('keeps the reason textarea at text-xs, matching its small reason-box size', () => {
    render(<ReviewActions onApprove={vi.fn()} onReject={vi.fn()} />)

    expect(screen.getByPlaceholderText(/reason for rejection/i).className).toContain('text-xs')
  })

  // Locked exception (epic close-out audit): Approve/Reject stay hand-rolled green/red, untouched.
  it('leaves the Approve/Reject buttons exactly as their locked hand-rolled green/red styling', () => {
    render(<ReviewActions onApprove={vi.fn()} onReject={vi.fn()} />)

    expect(screen.getByRole('button', { name: 'Approve' }).className).toContain('bg-emerald-600')
    expect(screen.getByRole('button', { name: 'Reject' }).className).toContain('bg-red-600')
  })

  it('has no accessibility violations', async () => {
    const { container } = render(<ReviewActions onApprove={vi.fn()} onReject={vi.fn()} />)
    expect(await axe(container)).toHaveNoViolations()
  })
})
