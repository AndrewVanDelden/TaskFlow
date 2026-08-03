import { useSortable } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import type { TaskItem } from '../types'
import { TaskCardView } from './TaskCardView'

// Sortable wrapper: owns the drag behavior and delegates the visuals to TaskCardView.
export function TaskCard({
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
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } =
    useSortable({ id: task.id })

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    // Fade the source while its copy rides in the DragOverlay.
    opacity: isDragging ? 0.4 : 1,
  }

  return (
    <div ref={setNodeRef} style={style} {...attributes} {...listeners} className="mb-2">
      <TaskCardView task={task} output={output} onApprove={onApprove} onReject={onReject} />
    </div>
  )
}
