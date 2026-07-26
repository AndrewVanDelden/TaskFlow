import { setupServer } from 'msw/node'
import { handlers } from './handlers'

// The interceptor that applies the default handlers. Individual tests can override a
// handler for their duration with server.use(...), undone by resetHandlers() in setup.
export const server = setupServer(...handlers)
