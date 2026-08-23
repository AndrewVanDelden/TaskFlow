import { useState } from 'react'
import type { TaskItem } from '../types'
import { useApplicationReview } from '../hooks/useApplicationReview'
import { MarkdownPreview } from './MarkdownPreview'
import { ReviewActions } from './ReviewActions'
import { ExportDownloadControls } from './ExportDownloadControls'

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

  const handleApprove = () => {
    setAttempted('approve')
    void approve()
  }

  const handleReject = (reason: string) => {
    setAttempted('reject')
    void reject(reason)
  }

  return (
    <div className="bg-slate-800 border border-slate-700 rounded-lg p-4 mb-2 space-y-4">
      <div>
        <h3 className="text-sm font-semibold text-white mb-2">Application review</h3>
      </div>

      <section>
        <h4 className="text-[10px] uppercase tracking-wide text-slate-500 mb-1">Base resume</h4>
        {baseResumeLoading && <p className="text-xs text-slate-400">Loading base resume...</p>}
        {baseResumeError && <p className="text-xs text-red-400" role="alert">{baseResumeError}</p>}
        {baseResume !== null && <MarkdownPreview content={baseResume} />}
      </section>

      <section>
        <h4 className="text-[10px] uppercase tracking-wide text-slate-500 mb-1">Tailored resume</h4>
        <MarkdownPreview content={resumeTask.tailoredContent ?? ''} />
        <ExportDownloadControls applicationId={applicationId} kind={resumeTask.kind} mode="preview" />
      </section>

      <section>
        <h4 className="text-[10px] uppercase tracking-wide text-slate-500 mb-1">Cover letter</h4>
        <MarkdownPreview content={coverLetterTask.tailoredContent ?? ''} />
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
