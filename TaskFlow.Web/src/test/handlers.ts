// The default fake backend. Wildcard origins (`*/...`) so a handler matches regardless of
// what VITE_API_BASE_URL resolves to in the test environment.
import { http, HttpResponse } from 'msw'
import type { TaskItem } from '../types'

export const handlers = [
  http.post('*/api/Auth/login', () =>
    HttpResponse.json({ token: 'fake.jwt.token', name: 'Ada', email: 'ada@x.dev', expiresAt: '' })),
  http.get('*/api/Tasks', () => HttpResponse.json<TaskItem[]>([])),
  http.get('*/api/AgentLogs', () => HttpResponse.json([])),
  http.post('*/api/Ingestion', () =>
    HttpResponse.json([
      { title: 'Draft from server', description: null, kind: 'Generic', section: 'Doc' },
    ])),
  http.post('*/api/Ingestion/commit', () => HttpResponse.json(1)),
  http.post('*/api/JobApplications/resume-context', () => HttpResponse.json(true)),
  http.get('*/api/JobApplications/:id/resume-context', () => HttpResponse.json('Base resume text')),
  http.post('*/api/JobApplications/:id/approve', () =>
    HttpResponse.json({
      id: 1, state: 'Approved', ingestionSessionId: '', ownerId: 1, createdAt: '', tasks: [],
    })),
  http.post('*/api/JobApplications/:id/reject', () =>
    HttpResponse.json({
      id: 1, state: 'Building', ingestionSessionId: '', ownerId: 1, createdAt: '', tasks: [],
    })),
  http.get('*/api/agents/executor', () => HttpResponse.json({ enabled: false })),
  http.post('*/api/agents/executor/enable', () => HttpResponse.json({ enabled: true })),
  http.post('*/api/agents/executor/disable', () => HttpResponse.json({ enabled: false })),
  http.post('*/api/Tasks/:id/approve', () =>
    HttpResponse.json({
      id: 1, title: 'Approved', description: null, status: 'Done', priority: 'High',
      dueDate: null, createdAt: '', updatedAt: '', assignedToId: null, assignedToName: null,
      kind: 'Generic', applicationId: null, tailoredContent: null,
    })),
  http.post('*/api/Tasks/:id/reject', () =>
    HttpResponse.json({
      id: 1, title: 'Rework', description: null, status: 'Todo', priority: 'High',
      dueDate: null, createdAt: '', updatedAt: '', assignedToId: null, assignedToName: null,
      kind: 'Generic', applicationId: null, tailoredContent: null,
    })),
]
