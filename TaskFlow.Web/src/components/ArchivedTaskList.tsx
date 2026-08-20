import type { TaskItem } from '../types'
import { formatDate } from '../lib/formatting'
import { taskKindLabel } from '../lib/taskKind'
import { ExportDownloadControls } from './ExportDownloadControls'
import { Button } from './ui/Button'
import { canDownloadExport } from '../lib/board'
import { borderDivider, textNeutral500 } from '../lib/tokens'

// Presentational list for the Archive view: mirrors AgentFeedList's own shape (an empty state plus
// one row per item). Each row carries a Restore button, and - for a Done+Approved Epic-3 sibling -
// the same ExportDownloadControls the board itself uses, so a previously archived resume/cover
// letter stays downloadable from here.
export function ArchivedTaskList({
  tasks,
  onRestore,
}: {
  tasks: TaskItem[]
  onRestore: (id: number) => void
}) {
  if (tasks.length === 0) {
    return <p className={`text-xs text-center py-8 ${textNeutral500}`}>Nothing archived yet.</p>
  }

  return (
    <ul>
      {tasks.map((task) => (
        <li key={task.id} className={`border-b ${borderDivider} py-3`}>
          <div className="flex items-start justify-between gap-2">
            <div>
              <div className="flex items-center gap-2">
                <span className="text-sm font-medium text-white">{task.title}</span>
                {task.kind !== 'Generic' && (
                  <span className="text-[10px] font-semibold px-2 py-0.5 rounded-full border border-indigo-700 bg-indigo-950 text-indigo-300">
                    {taskKindLabel(task.kind)}
                  </span>
                )}
              </div>
              <div className={`text-xs ${textNeutral500} mt-1`}>
                {task.company ?? '—'}
                {task.archivedAt && <> · Archived {formatDate(task.archivedAt)}</>}
              </div>
            </div>
            <Button variant="ghost" onClick={() => onRestore(task.id)} className="shrink-0">
              Restore
            </Button>
          </div>

          {canDownloadExport(task) && (
            <ExportDownloadControls applicationId={task.applicationId} kind={task.kind} />
          )}
        </li>
      ))}
    </ul>
  )
}
