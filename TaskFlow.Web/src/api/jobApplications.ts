import type { JobApplicationResponse } from '../types'
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
