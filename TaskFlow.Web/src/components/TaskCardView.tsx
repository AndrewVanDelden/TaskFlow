import type { TaskItem } from '../types'
import { priorityStyles } from '../lib/styles'
import { formatDate } from '../lib/formatting'
import { ReviewActions } from './ReviewActions'
import { ExportDownloadControls } from './ExportDownloadControls'

// Presentational card with no drag behavior, so it can render both inside the sortable TaskCard
// and inside the DragOverlay (which has no SortableContext). On Review cards it shows the executor's
// output and the Approve/Reject controls (when onApprove + onReject are supplied).
export function TaskCardView({
  task,
  output,
  onApprove,
  onReject,
}: {
  task: TaskItem
  output?: string[]
  onApprove?: () => void
  onReject?: (reason: string) => void
}) {
  return (
    <div className="bg-slate-800 border border-slate-700 rounded-lg p-3 cursor-grab active:cursor-grabbing hover:border-slate-600">
      <div className="flex items-start justify-between gap-2 mb-2">
        <h3 className="text-sm font-medium text-white leading-snug">{task.title}</h3>
        <span
          className={`text-[10px] font-semibold px-2 py-0.5 rounded-full border shrink-0 ${
            priorityStyles[task.priority] ?? priorityStyles.Low
          }`}
        >
          {task.priority}
        </span>
      </div>

      {task.description && (
        <p className="text-xs text-slate-400 mb-2 line-clamp-2">{task.description}</p>
      )}

      <div className="flex items-center justify-between text-[11px] text-slate-500">
        <span>{task.assignedToName ?? 'Unassigned'}</span>
        {task.dueDate && <span>{formatDate(task.dueDate)}</span>}
      </div>

      {output && output.length > 0 && (
        <div className="mt-2 rounded bg-slate-900/70 border border-slate-700 p-2 space-y-1">
          <div className="text-[10px] uppercase tracking-wide text-slate-500">Executor output</div>
          {output.map((line, i) => (
            <p key={i} className="text-xs text-slate-300 whitespace-pre-wrap">
              {line}
            </p>
          ))}
        </div>
      )}

      {onApprove && onReject && <ReviewActions onApprove={onApprove} onReject={onReject} />}

      {task.status === 'Done' && task.applicationId !== null && (
        <ExportDownloadControls applicationId={task.applicationId} kind={task.kind} />
      )}
    </div>
  )
}
