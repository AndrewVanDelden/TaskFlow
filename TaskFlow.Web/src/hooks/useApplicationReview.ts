import { useEffect, useState } from 'react'
import { getApplicationResumeContext, approveApplication, rejectApplication } from '../api/jobApplications'

// Owns the review-card state for one ReviewReady application: the base resume (fetched once on
// mount) plus the approve/reject actions, each with their own loading/error state so a failed
// action doesn't clobber the already-loaded base resume. Mirrors useBaseResumeCapture's shape.
export function useApplicationReview(applicationId: number) {
  const [baseResume, setBaseResume] = useState<string | null>(null)
  const [baseResumeLoading, setBaseResumeLoading] = useState(true)
  const [baseResumeError, setBaseResumeError] = useState<string | null>(null)

  const [actionLoading, setActionLoading] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    setBaseResumeLoading(true)
    setBaseResumeError(null)
    // Clear the previous application's resume too - otherwise a caller that reuses this hook
    // across different applicationIds would briefly (or, on error, indefinitely) keep showing the
    // wrong application's content (PR #45 review finding).
    setBaseResume(null)
    // Also clear any leftover action state from the previous application (PR #45 review, round 2)
    // - otherwise a prior approve/reject error, or an in-flight loading flag, would leak into the
    // new application's UI.
    setActionLoading(false)
    setActionError(null)
    getApplicationResumeContext(applicationId)
      .then((content) => {
        if (cancelled) return
        setBaseResume(content)
      })
      .catch((err) => {
        if (cancelled) return
        setBaseResumeError(err instanceof Error ? err.message : 'Failed to load the base resume.')
      })
      .finally(() => {
        if (cancelled) return
        setBaseResumeLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [applicationId])

  const approve = async () => {
    setActionLoading(true)
    setActionError(null)
    try {
      await approveApplication(applicationId)
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'Failed to approve the application.')
    } finally {
      setActionLoading(false)
    }
  }

  const reject = async (reason: string) => {
    setActionLoading(true)
    setActionError(null)
    try {
      await rejectApplication(applicationId, reason)
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'Failed to reject the application.')
    } finally {
      setActionLoading(false)
    }
  }

  return {
    baseResume,
    baseResumeLoading,
    baseResumeError,
    approve,
    reject,
    actionLoading,
    actionError,
  }
}
