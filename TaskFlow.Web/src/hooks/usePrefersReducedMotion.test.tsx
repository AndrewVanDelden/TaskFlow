import { renderHook, render, screen, act } from '@testing-library/react'
import { describe, it, expect, vi } from 'vitest'
import { usePrefersReducedMotion } from './usePrefersReducedMotion'
import { mockPrefersReducedMotion } from '../test/reducedMotion'

function Demo() {
  const prefersReducedMotion = usePrefersReducedMotion()
  const className = prefersReducedMotion ? 'opacity-100' : 'animate-pulse'
  return <div data-testid="pulse" className={className} />
}

describe('usePrefersReducedMotion', () => {
  it('returns true when the OS setting requests reduced motion', () => {
    mockPrefersReducedMotion(true)
    const { result } = renderHook(() => usePrefersReducedMotion())

    expect(result.current).toBe(true)
  })

  it('returns false when the OS setting does not request reduced motion', () => {
    mockPrefersReducedMotion(false)
    const { result } = renderHook(() => usePrefersReducedMotion())

    expect(result.current).toBe(false)
  })

  it('renders the static end-state class under reduced motion', () => {
    mockPrefersReducedMotion(true)
    render(<Demo />)

    expect(screen.getByTestId('pulse').className).toBe('opacity-100')
  })

  it('renders the animating class when motion is not reduced', () => {
    mockPrefersReducedMotion(false)
    render(<Demo />)

    expect(screen.getByTestId('pulse').className).toBe('animate-pulse')
  })

  it('updates when the OS setting changes live, without remounting', () => {
    const control = mockPrefersReducedMotion(false)
    const { result } = renderHook(() => usePrefersReducedMotion())
    expect(result.current).toBe(false)

    act(() => {
      control.fireChange(true)
    })

    expect(result.current).toBe(true)
  })

  // PR #61 review finding: every caller (ExecutorControl, TailorButton, TaskCardView - the latter
  // once per rendered card) registered its own independent matchMedia query and change listener.
  // With 20-30 cards on a board that's 20-30 duplicate identical subscriptions. Refactored to a
  // single shared module-level subscription via useSyncExternalStore - this proves the sharing
  // actually works by spying on window.matchMedia itself (not the mock wrapper) and asserting the
  // real browser API is invoked only once no matter how many components call the hook.
  it('shares a single underlying matchMedia subscription across multiple simultaneously-mounted callers', () => {
    mockPrefersReducedMotion(false)
    const matchMediaSpy = vi.spyOn(window, 'matchMedia')

    function TwoConsumers() {
      const first = usePrefersReducedMotion()
      const second = usePrefersReducedMotion()
      return (
        <div>
          <span data-testid="first">{String(first)}</span>
          <span data-testid="second">{String(second)}</span>
        </div>
      )
    }

    render(<TwoConsumers />)
    renderHook(() => usePrefersReducedMotion())

    const reducedMotionCalls = matchMediaSpy.mock.calls.filter(
      ([query]) => query === '(prefers-reduced-motion: reduce)',
    )
    expect(reducedMotionCalls).toHaveLength(1)
  })
})
