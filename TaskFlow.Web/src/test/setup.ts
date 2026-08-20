// Runs before each test file (wired via vite.config.ts `test.setupFiles`).
// Loads the jest-dom matchers and starts the MSW server so every test intercepts fetch.
// Also injects the real Tailwind stylesheet so component tests' getComputedStyle assertions see
// real styling. Normally only main.tsx imports index.css, and a plain import isn't enough either:
// jsdom parses `@layer` blocks but doesn't apply their rules to computed style, and every
// Tailwind v4 utility class is generated inside `@layer utilities` — so the raw CSS is stripped
// of its layers (see stripCssLayers.ts) before being injected.
import '@testing-library/jest-dom'
import { beforeAll, beforeEach, afterEach, afterAll } from 'vitest'
import { server } from './server'
import rawIndexCss from '../index.css?inline'
import { stripCssLayers } from './stripCssLayers'
import { mockPrefersReducedMotion } from './reducedMotion'

beforeAll(() => {
  const style = document.createElement('style')
  style.textContent = stripCssLayers(rawIndexCss)
  document.head.appendChild(style)
})

// Start intercepting before any test. `error` fails a test that hits an undefined handler,
// so a forgotten mock surfaces loudly instead of silently returning nothing.
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))

// jsdom has no real window.matchMedia at all - any component calling usePrefersReducedMotion()
// (even indirectly, e.g. a card rendered inside a board/column) throws unless it's stubbed.
// Defaults every test to "motion not reduced"; a test that specifically cares about the reduced
// state calls mockPrefersReducedMotion(true) itself, which simply overrides this for that test.
beforeEach(() => {
  mockPrefersReducedMotion(false)
})

// Undo per-test handler overrides and clear stored auth state so tests stay isolated.
afterEach(() => {
  server.resetHandlers()
  localStorage.clear()
})

afterAll(() => server.close())
