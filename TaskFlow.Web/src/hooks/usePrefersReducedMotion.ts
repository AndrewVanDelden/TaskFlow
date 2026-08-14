import { useState } from 'react'

export function usePrefersReducedMotion(): boolean {
  const [prefersReducedMotion] = useState(
    () => window.matchMedia('(prefers-reduced-motion: reduce)').matches,
  )
  return prefersReducedMotion
}
