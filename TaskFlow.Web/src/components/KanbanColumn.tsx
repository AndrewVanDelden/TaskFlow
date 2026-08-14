import { useDroppable } from '@dnd-kit/core'
import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable'
import type { TaskItem, TaskStatus } from '../types'
import { groupSiblingCards, type ApplicationPair } from '../lib/board'
import { TaskCard } from './TaskCard'
import { ApplicationReviewCard } from './ApplicationReviewCard'
import { ColumnHeader } from './ui/ColumnHeader'

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

  const renderCard = (task: TaskItem) => (
    <TaskCard
      key={task.id}
      task={task}
      output={outputFor?.(task.id)}
      onApprove={onApprove ? () => onApprove(task.id) : undefined}
      onReject={onReject ? (reason) => onReject(task.id, reason) : undefined}
    />
  )

  return (
    <div
      ref={setNodeRef}
      className={`flex-1 min-w-[240px] bg-slate-900/60 rounded-xl p-3 border transition-colors ${
        isOver ? 'border-blue-500' : 'border-slate-800'
      }`}
    >
      <ColumnHeader label={label} count={tasks.length} />

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
              role="group"
              aria-label="Job application"
              className="border border-indigo-800/60 rounded-lg p-1 mb-2"
            >
              {group.map(renderCard)}
            </div>
          ) : (
            group.map(renderCard)
          ),
        )}
      </SortableContext>

      {tasks.length === 0 && (!pairs || pairs.length === 0) && (
        <p className="text-xs text-slate-600 text-center py-6">No tasks</p>
      )}
    </div>
  )
}
