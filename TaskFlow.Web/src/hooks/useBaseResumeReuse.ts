import { useEffect, useState } from 'react'
import { getMostRecentResumeContext } from '../api/jobApplications'

export function useBaseResumeReuse() {
  const [available, setAvailable] = useState(false)
  const [content, setContent] = useState('')
  const [updatedAt, setUpdatedAt] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    getMostRecentResumeContext()
      .then((summary) => {
        if (cancelled) return
        setAvailable(true)
        setContent(summary.content)
        setUpdatedAt(summary.updatedAt)
      })
      .catch(() => {
        // Optional nicety, not a required capability - any failure (404 = never saved one, or a
        // real error) just means nothing to offer. Never surfaced as an error state.
      })
    return () => {
      cancelled = true
    }
  }, [])

  return { available, content, updatedAt }
}
