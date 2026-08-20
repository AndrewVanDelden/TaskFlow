import { useSyncExternalStore } from 'react'

const REDUCED_MOTION_QUERY = '(prefers-reduced-motion: reduce)'

// Module-level singleton: every caller (ExecutorControl, TailorButton, and TaskCardView - the
// latter once per rendered card, so 20-30 cards previously meant 20-30 duplicate identical
// matchMedia subscriptions) shares exactly one MediaQueryList and one 'change' listener via
// useSyncExternalStore, the modern primitive for subscribing multiple components to one external
// browser API without duplicating the subscription or risking tearing under concurrent rendering.
let cachedMatchMediaFn: typeof window.matchMedia | null = null
let cachedMediaQueryList: MediaQueryList | null = null
const storeListeners = new Set<() => void>()

// Re-derives the shared MediaQueryList whenever `window.matchMedia` itself changes identity (only
// ever happens in tests, which replace window.matchMedia with a fresh mock per test/case) - real
// browsers never reassign it, so this is a no-op rebuild check in production.
function ensureMediaQueryList(): MediaQueryList {
  if (cachedMediaQueryList === null || cachedMatchMediaFn !== window.matchMedia) {
    cachedMatchMediaFn = window.matchMedia
    cachedMediaQueryList = window.matchMedia(REDUCED_MOTION_QUERY)
  }
  return cachedMediaQueryList
}

function notifyStoreListeners(): void {
  storeListeners.forEach((listener) => listener())
}

function subscribe(onStoreChange: () => void): () => void {
  const mediaQueryList = ensureMediaQueryList()
  storeListeners.add(onStoreChange)

  if (storeListeners.size === 1) {
    mediaQueryList.addEventListener('change', notifyStoreListeners)
  }

  return () => {
    storeListeners.delete(onStoreChange)
    if (storeListeners.size === 0) {
      mediaQueryList.removeEventListener('change', notifyStoreListeners)
    }
  }
}

function getSnapshot(): boolean {
  return ensureMediaQueryList().matches
}

export function usePrefersReducedMotion(): boolean {
  return useSyncExternalStore(subscribe, getSnapshot)
}
