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
