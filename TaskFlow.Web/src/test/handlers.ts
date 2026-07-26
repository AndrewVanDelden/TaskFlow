// The default fake backend. Wildcard origins (`*/...`) so a handler matches regardless of
// what VITE_API_BASE_URL resolves to in the test environment.
import { http, HttpResponse } from 'msw'

export const handlers = [
  http.post('*/api/Auth/login', () =>
    HttpResponse.json({ token: 'fake.jwt.token', name: 'Ada', email: 'ada@x.dev', expiresAt: '' })),
  http.get('*/api/Tasks', () => HttpResponse.json([])),
  http.get('*/api/AgentLogs', () => HttpResponse.json([])),
]
