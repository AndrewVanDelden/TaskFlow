// defineConfig comes from 'vitest/config' (not 'vite') so the `test` block below is typed.
// It is the same function with Vitest's options merged in.
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// PR #66 review finding: Vite restarts its dev server (re-evaluating this file) whenever
// vite.config.ts or a watched .env file changes, WITHOUT relaunching the Node process or the
// `.\run`/`npm run dev` invocation that started it. A bare `Date.now()` here would treat every one
// of those internal restarts as a fresh `.\run` too, force-logging out an actively-iterating
// developer just for tweaking a plugin option. process.env persists across that kind of restart
// (same OS process, same env) but not across a genuinely new invocation (a new process each time),
// so it doubles as a once-per-process cache: computed on first evaluation, reused on every
// config-triggered restart within that same process.
const bootId = process.env.APP_BOOT_ID ?? (process.env.APP_BOOT_ID = String(Date.now()))

export default defineConfig({
  plugins: [react(), tailwindcss()],
  define: {
    // A fresh id baked into the bundle once per real dev-server invocation (see bootId above), so
    // lib/devAuthReset.ts can tell "the dev server just restarted" apart from "the page was
    // refreshed during an ongoing session" - see that file for why.
    __APP_BOOT_ID__: JSON.stringify(bootId),
  },
  server: {
    port: 5173,
    // Single-origin dev: the browser talks only to :5173, and Vite forwards API calls and the SignalR
    // hub to the API on :5002. This removes the CORS/env dance and gives one URL for the whole app.
    proxy: {
      '/api': 'http://localhost:5002',
      '/hubs': { target: 'http://localhost:5002', ws: true },
    },
  },
  test: {
    globals: true,                      // use describe/it/expect without importing them
    environment: 'jsdom',               // fake browser DOM so components render in Node
    setupFiles: './src/test/setup.ts',  // runs once before each test file
    css: true,                          // process CSS imports instead of erroring on them
    coverage: {
      provider: 'v8',
      reporter: ['text', 'html'],
      include: ['src/**/*.{ts,tsx}'],
      // Exclude tests, the entry point, test scaffolding, and type-only files from the denominator.
      exclude: ['src/**/*.test.{ts,tsx}', 'src/main.tsx', 'src/test/**', 'src/**/*.d.ts'],
    },
  },
})