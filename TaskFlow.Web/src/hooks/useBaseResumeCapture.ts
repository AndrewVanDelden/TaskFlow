import { useState } from 'react'
import { saveResumeContext } from '../api/jobApplications'

// Owns the save/loading/error/confirmation state for the base-resume capture control. Kept
// separate from useIngestion: this hits a different endpoint and has no drafts/commit concept.
export function useBaseResumeCapture() {
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  const save = async (ingestionSessionId: string, content: string) => {
    setLoading(true)
    setError(null)
    setSaved(false)
    try {
      await saveResumeContext(ingestionSessionId, content)
      setSaved(true)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save the base resume.')
    } finally {
      setLoading(false)
    }
  }

  return { loading, error, saved, save }
}
