// Runs before each test file (wired via vite.config.ts `test.setupFiles`).
// Loads the jest-dom matchers and starts the MSW server so every test intercepts fetch.
// Also injects the real Tailwind stylesheet so component tests' getComputedStyle assertions see
// real styling. Normally only main.tsx imports index.css, and a plain import isn't enough either:
// jsdom parses `@layer` blocks but doesn't apply their rules to computed style, and every
// Tailwind v4 utility class is generated inside `@layer utilities` — so the raw CSS is stripped
// of its layers (see stripCssLayers.ts) before being injected.
import '@testing-library/jest-dom'
import { beforeAll, afterEach, afterAll } from 'vitest'
import { server } from './server'
import rawIndexCss from '../index.css?inline'
import { stripCssLayers } from './stripCssLayers'

beforeAll(() => {
  const style = document.createElement('style')
  style.textContent = stripCssLayers(rawIndexCss)
  document.head.appendChild(style)
})

// Start intercepting before any test. `error` fails a test that hits an undefined handler,
// so a forgotten mock surfaces loudly instead of silently returning nothing.
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))

// Undo per-test handler overrides and clear stored auth state so tests stay isolated.
afterEach(() => {
  server.resetHandlers()
  localStorage.clear()
})

afterAll(() => server.close())
