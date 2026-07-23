import { useDroppable } from '@dnd-kit/core'
import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable'
import type { TaskItem, TaskStatus } from './types'
import { TaskCard } from './TaskCard'

interface Props {
  status: TaskStatus
  label: string
  tasks: TaskItem[]
}

export function KanbanColumn({ status, label, tasks }: Props) {
  const { setNodeRef, isOver } = useDroppable({ id: status })

  return (
    <div
      ref={setNodeRef}
      className={`flex-1 min-w-[240px] bg-slate-900/60 rounded-xl p-3 border transition-colors ${
        isOver ? 'border-blue-500' : 'border-slate-800'
      }`}
    >
      <div className="flex items-center justify-between mb-3">
        <h2 className="text-sm font-semibold text-slate-300">{label}</h2>
        <span className="text-xs text-slate-500 bg-slate-800 px-2 py-0.5 rounded-full">
          {tasks.length}
        </span>
      </div>

      <SortableContext
        items={tasks.map((t) => t.id)}
        strategy={verticalListSortingStrategy}
      >
        {tasks.map((task) => (
          <TaskCard key={task.id} task={task} />
        ))}
      </SortableContext>

      {tasks.length === 0 && (
        <p className="text-xs text-slate-600 text-center py-6">No tasks</p>
      )}
    </div>
  )
}