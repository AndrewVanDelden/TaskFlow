import { vi } from 'vitest'

// Shared mock every later sprint's animated-component tests import — jsdom has no real
// window.matchMedia, so calling it unmocked throws/returns undefined behavior instead of a result.
// Also tracks the registered 'change' listener so tests can simulate the OS setting flipping
// live via the returned `fireChange` helper.
export function mockPrefersReducedMotion(matches: boolean) {
  let currentMatches = matches
  let changeListener: ((event: { matches: boolean }) => void) | null = null

  window.matchMedia = vi.fn().mockImplementation((query: string) => ({
    get matches() {
      return query === '(prefers-reduced-motion: reduce)' ? currentMatches : false
    },
    media: query,
    onchange: null,
    addEventListener: vi.fn((eventName: string, listener: (event: { matches: boolean }) => void) => {
      if (eventName === 'change' && query === '(prefers-reduced-motion: reduce)') {
        changeListener = listener
      }
    }),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  })) as unknown as typeof window.matchMedia

  return {
    fireChange(nextMatches: boolean) {
      currentMatches = nextMatches
      changeListener?.({ matches: nextMatches })
    },
  }
}
