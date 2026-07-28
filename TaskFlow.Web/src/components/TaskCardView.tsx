import type { TaskItem } from '../types'
import { priorityStyles } from '../lib/styles'
import { formatDate } from '../lib/formatting'

// Presentational card with no drag behavior, so it can render both inside the sortable TaskCard
// and inside the DragOverlay (which has no SortableContext).
export function TaskCardView({ task }: { task: TaskItem }) {
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
    </div>
  )
}
