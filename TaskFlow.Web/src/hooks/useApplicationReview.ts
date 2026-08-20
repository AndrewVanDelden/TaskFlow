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

  // Resetting every piece of state when applicationId changes is responding to a prop change, not
  // a side effect - done during render (React's own documented pattern for this) rather than at
  // the top of the fetch effect below, so it can't trigger that effect's own render-then-effect-
  // then-render cascade. Clearing the previous application's resume (PR #45 review finding) and
  // any leftover action state (PR #45 review, round 2) both still apply - a caller that reuses this
  // hook across different applicationIds must never briefly show the wrong application's content.
  const [reviewedApplicationId, setReviewedApplicationId] = useState(applicationId)
  if (applicationId !== reviewedApplicationId) {
    setReviewedApplicationId(applicationId)
    setBaseResumeLoading(true)
    setBaseResumeError(null)
    setBaseResume(null)
    setActionLoading(false)
    setActionError(null)
  }

  useEffect(() => {
    let cancelled = false
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

  // Shared by approve/reject: they differ only in which API call runs and the fallback message.
  const runAction = async (action: () => Promise<unknown>, fallbackMessage: string) => {
    setActionLoading(true)
    setActionError(null)
    try {
      await action()
    } catch (err) {
      setActionError(err instanceof Error ? err.message : fallbackMessage)
    } finally {
      setActionLoading(false)
    }
  }

  const approve = () =>
    runAction(() => approveApplication(applicationId), 'Failed to approve the application.')

  const reject = (reason: string) =>
    runAction(() => rejectApplication(applicationId, reason), 'Failed to reject the application.')

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
