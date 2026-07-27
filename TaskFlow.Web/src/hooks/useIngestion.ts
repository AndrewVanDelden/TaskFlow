import { useState } from 'react'
import type { TaskDraft } from '../types'
import { parseDocument } from '../api/ingestion'

// Owns the submit-and-preview state so the container stays presentational.
export function useIngestion() {
  const [drafts, setDrafts] = useState<TaskDraft[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async (content: string) => {
    setLoading(true)
    setError(null)
    try {
      setDrafts(await parseDocument(content))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to parse the document.')
    } finally {
      setLoading(false)
    }
  }

  return { drafts, loading, error, submit }
}
