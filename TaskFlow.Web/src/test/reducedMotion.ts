import { vi } from 'vitest'

// Shared mock every later sprint's animated-component tests import — jsdom has no real
// window.matchMedia, so calling it unmocked throws/returns undefined behavior instead of a result.
export function mockPrefersReducedMotion(matches: boolean) {
  window.matchMedia = vi.fn().mockImplementation((query: string) => ({
    matches: query === '(prefers-reduced-motion: reduce)' ? matches : false,
    media: query,
    onchange: null,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  })) as unknown as typeof window.matchMedia
}
