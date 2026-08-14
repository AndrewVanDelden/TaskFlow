import type { JobApplicationResponse, ResumeContextSummary, TaskDraft } from '../types'
import { request, requestBlob, type DownloadedFile } from './client'

// Saves the user's base resume server-side, scoped to one ingestion session — never localStorage,
// since a server-side agent (Sprint 3R) cannot read browser storage.
export function saveResumeContext(ingestionSessionId: string, content: string): Promise<boolean> {
  return request<boolean>('/api/JobApplications/resume-context', {
    method: 'POST',
    body: JSON.stringify({ ingestionSessionId, content }),
  })
}

// The base resume text for the review card, fetched by application id. 404 (not found / not
// owned / no resume saved) surfaces as a rejected promise — callers show a single error state.
export function getApplicationResumeContext(applicationId: number): Promise<string> {
  return request<string>(`/api/JobApplications/${applicationId}/resume-context`)
}

// The caller's own most recently saved base resume, from any session (Sprint 6 reuse offer).
// 404 (never saved one) surfaces as a rejected promise, same convention as getApplicationResumeContext.
export function getMostRecentResumeContext(): Promise<ResumeContextSummary> {
  return request<ResumeContextSummary>('/api/JobApplications/resume-context/latest')
}

// Parses a pasted job posting into title/company/requirements drafts (does not persist anything).
export function parseJobPosting(content: string): Promise<TaskDraft[]> {
  return request<TaskDraft[]>('/api/JobApplications/parse', {
    method: 'POST',
    body: JSON.stringify({ content }),
  })
}

// Assembles the parsed posting into a JobApplication with two Todo sibling tasks (resume + cover letter).
export function assembleApplication(
  ingestionSessionId: string,
  posting: { title: string; description: string | null; section: string; company: string | null },
): Promise<JobApplicationResponse> {
  return request<JobApplicationResponse>('/api/JobApplications', {
    method: 'POST',
    body: JSON.stringify({ ingestionSessionId, posting }),
  })
}

// Human sign-off on the combined resume + cover-letter pair: ReviewReady -> Approved.
export function approveApplication(applicationId: number): Promise<JobApplicationResponse> {
  return request<JobApplicationResponse>(`/api/JobApplications/${applicationId}/approve`, {
    method: 'POST',
  })
}

// Human rejection of the pair, with a required reason: ReviewReady -> Building (rework).
export function rejectApplication(applicationId: number, reason: string): Promise<JobApplicationResponse> {
  return request<JobApplicationResponse>(`/api/JobApplications/${applicationId}/reject`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  })
}

export type ExportFormat = 'pdf' | 'markdown'

// Downloads the tailored resume (PDF or Markdown) for an Approved application's Done task.
export function exportResume(applicationId: number, format: ExportFormat): Promise<DownloadedFile> {
  return requestBlob(`/api/JobApplications/${applicationId}/export/resume?format=${format}`)
}

// Downloads the tailored cover letter (PDF or Markdown) for an Approved application's Done task.
export function exportCoverLetter(applicationId: number, format: ExportFormat): Promise<DownloadedFile> {
  return requestBlob(`/api/JobApplications/${applicationId}/export/cover-letter?format=${format}`)
}
