import { request } from './client'

// Saves the user's base resume server-side, scoped to one ingestion session — never localStorage,
// since a server-side agent (Sprint 3R) cannot read browser storage.
export function saveResumeContext(ingestionSessionId: string, content: string): Promise<boolean> {
  return request<boolean>('/api/JobApplications/resume-context', {
    method: 'POST',
    body: JSON.stringify({ ingestionSessionId, content }),
  })
}
