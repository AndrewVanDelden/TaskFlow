import { useSortable } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import type { TaskItem } from './types'

const priorityStyles: Record<string, string> = {
  High: 'bg-red-500/15 text-red-300 border-red-500/30',
  Medium: 'bg-amber-500/15 text-amber-300 border-amber-500/30',
  Low: 'bg-slate-500/15 text-slate-300 border-slate-500/30',
}

export function TaskCard({ task }: { task: TaskItem }) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } =
    useSortable({ id: task.id })

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.4 : 1,
  }

  return (
    <div
      ref={setNodeRef}
      style={style}
      {...attributes}
      {...listeners}
      className="bg-slate-800 border border-slate-700 rounded-lg p-3 mb-2 cursor-grab active:cursor-grabbing hover:border-slate-600"
    >
      <div className="flex items-start justify-between gap-2 mb-2">
        <h3 className="text-sm font-medium text-white leading-snug">
          {task.title}
        </h3>
        <span
          className={`text-[10px] font-semibold px-2 py-0.5 rounded-full border shrink-0 ${
            priorityStyles[task.priority] ?? priorityStyles.Low
          }`}
        >
          {task.priority}
        </span>
      </div>

      {task.description && (
        <p className="text-xs text-slate-400 mb-2 line-clamp-2">
          {task.description}
        </p>
      )}

      <div className="flex items-center justify-between text-[11px] text-slate-500">
        <span>{task.assignedToName ?? 'Unassigned'}</span>
        {task.dueDate && (
          <span>{new Date(task.dueDate).toLocaleDateString()}</span>
        )}
      </div>
    </div>
  )
}