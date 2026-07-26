// Runs before each test file (wired via vite.config.ts `test.setupFiles`).
// Loads the jest-dom matchers and starts the MSW server so every test intercepts fetch.
import '@testing-library/jest-dom'
import { beforeAll, afterEach, afterAll } from 'vitest'
import { server } from './server'

// Start intercepting before any test. `error` fails a test that hits an undefined handler,
// so a forgotten mock surfaces loudly instead of silently returning nothing.
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))

// Undo per-test handler overrides and clear stored auth state so tests stay isolated.
afterEach(() => {
  server.resetHandlers()
  localStorage.clear()
})

afterAll(() => server.close())
