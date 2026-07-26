// Runs once before each test file (wired via vite.config.ts `test.setupFiles`).
// Right now it only loads the jest-dom matchers (e.g. .toBeInTheDocument()).
// Slice K adds the MSW server start/stop here.
import '@testing-library/jest-dom'
