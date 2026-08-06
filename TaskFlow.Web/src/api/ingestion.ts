import type { TaskDraft } from '../types'
import { request } from './client'

// Source-agnostic: the caller obtains the content however it likes (paste, file, link) and
// sends the text. POST /api/Ingestion returns the parsed drafts.
export function parseDocument(content: string): Promise<TaskDraft[]> {
  return request<TaskDraft[]>('/api/Ingestion', {
    method: 'POST',
    body: JSON.stringify({ content }),
  })
}

// Commit approved drafts to the board. Returns the number of tasks created.
export function commitDrafts(sourceName: string, drafts: TaskDraft[]): Promise<number> {
  return request<number>('/api/Ingestion/commit', {
    method: 'POST',
    body: JSON.stringify({ sourceName, drafts }),
  })
}
