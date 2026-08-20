import { useArchivedTasks } from '../hooks/useArchivedTasks'
import { ArchivedTaskList } from '../components/ArchivedTaskList'

// Thin page, same shape as Activity.tsx: full-height, no 300px-rail sizing constraint. Fetches
// archived tasks on mount via useArchivedTasks and lets ArchivedTaskList restore them.
export function Archive() {
  const { tasks, error, restore } = useArchivedTasks()

  return (
    <main className="p-6">
      <h1 className="text-2xl font-semibold tracking-tight text-[#e9e9ed] mb-4">Archive</h1>
      {error && (
        <div role="alert" className="mb-3 text-sm text-red-400 bg-red-950 border border-red-900 rounded px-3 py-2">
          {error}
        </div>
      )}
      <ArchivedTaskList tasks={tasks} onRestore={restore} />
    </main>
  )
}
