// defineConfig comes from 'vitest/config' (not 'vite') so the `test` block below is typed.
// It is the same function with Vitest's options merged in.
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
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
  },
})