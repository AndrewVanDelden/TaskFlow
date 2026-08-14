import type { TaskItem } from '../types'
import { formatDate } from '../lib/formatting'
import { taskKindLabel } from '../lib/taskKind'
import { ReviewActions } from './ReviewActions'
import { ExportDownloadControls } from './ExportDownloadControls'
import { usePrefersReducedMotion } from '../hooks/usePrefersReducedMotion'
import { bgSurface, textAccent200, textAccent300, textNeutral500 } from '../lib/tokens'

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
  const prefersReducedMotion = usePrefersReducedMotion()
  const isInProgress = task.status === 'InProgress'

  return (
    <div
      className={`relative ${bgSurface} border border-white/10 rounded-xl p-4 cursor-grab active:cursor-grabbing hover:border-slate-600`}
    >
      <div className="flex items-start justify-between gap-2 mb-2">
        <h3 className="text-sm font-medium text-white leading-snug">{task.title}</h3>
        <div className="flex items-center gap-1 shrink-0">
          {task.kind !== 'Generic' && (
            <span className="text-[10px] font-semibold px-2 py-0.5 rounded-full border border-indigo-700 bg-indigo-950 text-indigo-300">
              {taskKindLabel(task.kind)}
            </span>
          )}
          <span className={`text-xs font-medium ${textAccent300}`}>{task.priority}</span>
        </div>
      </div>

      {task.description && (
        <p className="text-xs text-slate-400 mb-2 line-clamp-2">{task.description}</p>
      )}

      <div className="flex items-center justify-between text-[11px] text-slate-500">
        <span className={`text-xs ${textNeutral500}`}>{task.company ?? '—'}</span>
        {task.dueDate && <span>{formatDate(task.dueDate)}</span>}
      </div>

      {isInProgress && (
        <p className={`text-xs ${textAccent200} mt-2`}>{`Tailoring ${taskKindLabel(task.kind)}…`}</p>
      )}

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

      {task.status === 'Done' && task.applicationId !== null && task.applicationState === 'Approved' && (
        <ExportDownloadControls applicationId={task.applicationId} kind={task.kind} />
      )}

      {isInProgress && (
        <div
          data-testid="progress-fill"
          className={`absolute inset-x-0 bottom-0 h-0.5 bg-gradient-to-r from-[#796cbf] to-[#d2cefd]${
            prefersReducedMotion ? '' : ' animate-pulse'
          }`}
        />
      )}
    </div>
  )
}
