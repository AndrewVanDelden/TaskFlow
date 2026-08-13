import { useDroppable } from '@dnd-kit/core'
import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable'
import type { TaskItem, TaskStatus } from '../types'
import { groupSiblingCards, type ApplicationPair } from '../lib/board'
import { TaskCard } from './TaskCard'
import { ApplicationReviewCard } from './ApplicationReviewCard'

interface Props {
  status: TaskStatus
  label: string
  tasks: TaskItem[]
  onApprove?: (id: number) => void
  onReject?: (id: number, reason: string) => void
  outputFor?: (id: number) => string[]
  // Review-column-only: ReviewReady application pairs, rendered as a combined review card instead
  // of two individual TaskCards. Omitted (undefined) for every other column — unchanged behavior.
  pairs?: ApplicationPair[]
}

export function KanbanColumn({ status, label, tasks, onApprove, onReject, outputFor, pairs }: Props) {
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

      {pairs && pairs.length > 0 && (
        <div className="mb-2">
          {pairs.map((pair) => (
            <ApplicationReviewCard
              key={pair.applicationId}
              applicationId={pair.applicationId}
              resumeTask={pair.resumeTask}
              coverLetterTask={pair.coverLetterTask}
            />
          ))}
        </div>
      )}

      <SortableContext
        items={tasks.map((t) => t.id)}
        strategy={verticalListSortingStrategy}
      >
        {groupSiblingCards(tasks).map((group) =>
          group.length === 2 ? (
            <div
              key={`pair-${group[0].applicationId}`}
              data-testid={`sibling-group-${group[0].applicationId}`}
              className="border border-indigo-800/60 rounded-lg p-1 mb-2"
            >
              {group.map((task) => (
                <TaskCard
                  key={task.id}
                  task={task}
                  output={outputFor?.(task.id)}
                  onApprove={onApprove ? () => onApprove(task.id) : undefined}
                  onReject={onReject ? (reason) => onReject(task.id, reason) : undefined}
                />
              ))}
            </div>
          ) : (
            group.map((task) => (
              <TaskCard
                key={task.id}
                task={task}
                output={outputFor?.(task.id)}
                onApprove={onApprove ? () => onApprove(task.id) : undefined}
                onReject={onReject ? (reason) => onReject(task.id, reason) : undefined}
              />
            ))
          ),
        )}
      </SortableContext>

      {tasks.length === 0 && (!pairs || pairs.length === 0) && (
        <p className="text-xs text-slate-600 text-center py-6">No tasks</p>
      )}
    </div>
  )
}
