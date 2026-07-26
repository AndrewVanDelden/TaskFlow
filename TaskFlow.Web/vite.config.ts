// defineConfig comes from 'vitest/config' (not 'vite') so the `test` block below is typed.
// It is the same function with Vitest's options merged in.
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
  },
  test: {
    globals: true,                      // use describe/it/expect without importing them
    environment: 'jsdom',               // fake browser DOM so components render in Node
    setupFiles: './src/test/setup.ts',  // runs once before each test file
    css: true,                          // process CSS imports instead of erroring on them
  },
})