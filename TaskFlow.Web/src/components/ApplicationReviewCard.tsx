import { useState } from 'react'
import type { TaskItem } from '../types'
import { useApplicationReview } from '../hooks/useApplicationReview'
import { ReviewActions } from './ReviewActions'
import { ExportDownloadControls } from './ExportDownloadControls'
import { openTextInNewTab } from '../lib/openTextInNewTab'
import { displayTitle } from '../lib/board'

// A ReviewReady application's combined review block: the base resume, the tailored resume, and
// the tailored cover letter, side by side with one Approve/Reject pair for the whole application.
// Static (no useSortable) — this is not a draggable kanban card, unlike TaskCard.
export function ApplicationReviewCard({
  applicationId,
  resumeTask,
  coverLetterTask,
}: {
  applicationId: number
  resumeTask: TaskItem
  coverLetterTask: TaskItem
}) {
  const { baseResume, baseResumeLoading, baseResumeError, approve, reject, actionLoading, actionError } =
    useApplicationReview(applicationId)

  // Tracks which action the user last triggered, so success can be shown for that action once the
  // hook settles (actionLoading false, actionError null) without duplicating its request logic.
  const [attempted, setAttempted] = useState<'approve' | 'reject' | null>(null)
  const succeeded = attempted !== null && !actionLoading && !actionError
  const [baseResumeViewError, setBaseResumeViewError] = useState<string | null>(null)

  const handleApprove = () => {
    setAttempted('approve')
    void approve()
  }

  const handleReject = (reason: string) => {
    setAttempted('reject')
    void reject(reason)
  }

  // User report (2026-08-22): the base resume has no PDF export of its own (it's the candidate's
  // own pasted text, not an AI-tailored artifact) - opening it directly, via openTextInNewTab,
  // gives it the same "click to view in a new tab" treatment the other two artifacts already have.
  const handleViewBaseResume = () => {
    if (baseResume === null) return
    const opened = openTextInNewTab(baseResume, 'Base resume')
    setBaseResumeViewError(
      opened ? null : 'Your browser blocked the preview. Please allow popups for this site and try again.',
    )
  }

  return (
    <div className="bg-slate-800 border border-slate-700 rounded-lg p-4 mb-2 space-y-4">
      <div>
        {/* User report (2026-08-24): a generic "Application review" heading gives no clue which
            application this card is for once several are open at once - names it after the same
            job title/company shown on the board card, via the shared displayTitle helper. */}
        <h3 className="text-sm font-semibold text-white mb-2">Application review — {displayTitle(resumeTask)}</h3>
      </div>

      {/* User report (2026-08-22): this card used to render all three artifacts' full raw content
          inline, making it many screen-heights tall - each section now shows only a "View" control
          that opens the real content in a new tab, matching every other card's compact size. */}
      <section>
        <h4 className="text-[10px] uppercase tracking-wide text-slate-500 mb-1">Base resume</h4>
        {baseResumeLoading && <p className="text-xs text-slate-400">Loading base resume...</p>}
        {baseResumeError && <p className="text-xs text-red-400" role="alert">{baseResumeError}</p>}
        {baseResume !== null && (
          <button
            type="button"
            onClick={handleViewBaseResume}
            className="bg-slate-700 hover:bg-slate-600 text-white text-xs font-semibold px-2 py-1 rounded"
          >
            View base resume
          </button>
        )}
        {baseResumeViewError && (
          <p className="text-xs text-red-400 mt-1" role="alert">
            {baseResumeViewError}
          </p>
        )}
      </section>

      <section>
        <h4 className="text-[10px] uppercase tracking-wide text-slate-500 mb-1">Tailored resume</h4>
        <ExportDownloadControls applicationId={applicationId} kind={resumeTask.kind} mode="preview" />
      </section>

      <section>
        <h4 className="text-[10px] uppercase tracking-wide text-slate-500 mb-1">Cover letter</h4>
        <ExportDownloadControls applicationId={applicationId} kind={coverLetterTask.kind} mode="preview" />
      </section>

      {actionError && (
        <p className="text-xs text-red-400" role="alert">
          {actionError}
        </p>
      )}
      {succeeded && (
        <p className="text-xs text-emerald-400">
          {attempted === 'approve' ? 'Approved.' : 'Rejected.'}
        </p>
      )}

      <ReviewActions onApprove={handleApprove} onReject={handleReject} />
    </div>
  )
}
