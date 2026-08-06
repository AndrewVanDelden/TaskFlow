import { useState } from 'react'
import type { TaskDraft } from '../types'
import { parseDocument, commitDrafts } from '../api/ingestion'

// Owns the submit/approve state so the container stays presentational.
export function useIngestion() {
  const [drafts, setDrafts] = useState<TaskDraft[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [committedCount, setCommittedCount] = useState<number | null>(null)

  // One place for the loading/error dance, so submit and approve don't duplicate it.
  const run = async (fallback: string, action: () => Promise<void>) => {
    setLoading(true)
    setError(null)
    try {
      await action()
    } catch (err) {
      setError(err instanceof Error ? err.message : fallback)
    } finally {
      setLoading(false)
    }
  }

  const submit = (content: string) =>
    run('Failed to parse the document.', async () => {
      setCommittedCount(null)
      setDrafts(await parseDocument(content))
    })

  const approve = (sourceName: string) =>
    run('Failed to commit the drafts.', async () => {
      const count = await commitDrafts(sourceName, drafts)
      setCommittedCount(count)
      setDrafts([])
    })

  return { drafts, loading, error, committedCount, submit, approve }
}
