import { renderHook, render, screen } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
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
})
