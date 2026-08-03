import { useState } from 'react'

// The review controls for a card: a reason box, Approve (green), and Reject (red). Reject stays
// disabled until a reason is typed. Owns the reason state so TaskCardView stays presentational.
export function ReviewActions({
  onApprove,
  onReject,
}: {
  onApprove: () => void
  onReject: (reason: string) => void
}) {
  const [reason, setReason] = useState('')
  const canReject = reason.trim().length > 0

  return (
    // Stop the drag sensor from treating typing/clicking here as the start of a drag.
    <div className="mt-3" onPointerDown={(e) => e.stopPropagation()}>
      <textarea
        value={reason}
        onChange={(e) => setReason(e.target.value)}
        placeholder="Reason for rejection (required to reject)"
        className="w-full h-16 p-2 rounded bg-slate-900 border border-slate-700 text-xs text-white placeholder-slate-500"
      />
      <div className="flex gap-2 mt-2">
        <button
          onClick={onApprove}
          className="flex-1 bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-semibold px-3 py-1.5 rounded"
        >
          Approve
        </button>
        <button
          onClick={() => onReject(reason.trim())}
          disabled={!canReject}
          className="flex-1 bg-red-600 hover:bg-red-500 disabled:opacity-40 disabled:cursor-not-allowed text-white text-xs font-semibold px-3 py-1.5 rounded"
        >
          Reject
        </button>
      </div>
    </div>
  )
}
