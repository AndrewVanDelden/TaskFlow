import { useDroppable } from '@dnd-kit/core'
import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable'
import type { TaskItem, TaskStatus } from '../types'
import { groupSiblingCards, type ApplicationPair } from '../lib/board'
import { TaskCard } from './TaskCard'
import { ApplicationReviewCard } from './ApplicationReviewCard'
import { ColumnHeader } from './ui/ColumnHeader'
import { Button } from './ui/Button'

interface Props {
  status: TaskStatus
  label: string
  tasks: TaskItem[]
  onApprove?: (id: number) => void
  onReject?: (id: number, reason: string) => void
  // Done-column-only: per-card archive (Board Done-column soft-archive).
  onArchive?: (id: number) => void
  // Done-column-only: bulk "Clear Done" action. Its presence is what makes the Clear Done button
  // render at all — the column doesn't need to know the caller only ever passes it for Done.
  onArchiveDone?: () => void
  outputFor?: (id: number) => string[]
  // Review-column-only: ReviewReady application pairs, rendered as a combined review card instead
  // of two individual TaskCards. Omitted (undefined) for every other column — unchanged behavior.
  pairs?: ApplicationPair[]
}

export function KanbanColumn({ status, label, tasks, onApprove, onReject, onArchive, onArchiveDone, outputFor, pairs }: Props) {
  const { setNodeRef, isOver } = useDroppable({ id: status })

  const renderCard = (task: TaskItem) => (
    <TaskCard
      key={task.id}
      task={task}
      output={outputFor?.(task.id)}
      onApprove={onApprove ? () => onApprove(task.id) : undefined}
      onReject={onReject ? (reason) => onReject(task.id, reason) : undefined}
      onArchive={onArchive ? () => onArchive(task.id) : undefined}
    />
  )

  // Native confirm() for the destructive bulk action - no existing custom confirmation-dialog
  // precedent in this codebase to match, and this matches the codebase's low-ceremony conventions
  // elsewhere.
  const handleClearDone = () => {
    if (window.confirm('Archive all Done tasks? You can restore them later from the Archive view.')) {
      onArchiveDone?.()
    }
  }

  return (
    <div
      ref={setNodeRef}
      className={`flex-1 min-w-[240px] bg-slate-900/60 rounded-xl p-3 border transition-colors ${
        isOver ? 'border-blue-500' : 'border-slate-800'
      }`}
    >
      <div className="flex items-center justify-between gap-2">
        <div className="flex-1 min-w-0">
          <ColumnHeader label={label} count={tasks.length} />
        </div>
        {status === 'Done' && onArchiveDone && (
          <Button variant="ghost" className="text-xs px-2 py-1 shrink-0" disabled={tasks.length === 0} onClick={handleClearDone}>
            Clear Done
          </Button>
        )}
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
