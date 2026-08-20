import { useAgentFeed } from '../hooks/useAgentFeed'
import { KanbanBoard } from './KanbanBoard'
import { AgentFeedList } from '../components/AgentFeedList'
import { AgentStatus } from '../components/AgentStatus'
import { ExecutorControl } from '../components/ExecutorControl'

// The Board screen. The header/nav and the page wrapper are provided by the ProtectedLayout.
export function Dashboard() {
  const { logs, cycles, connected } = useAgentFeed()

  return (
    <main className="p-6">
      <ExecutorControl />

      <div className="grid grid-cols-1 xl:grid-cols-[1fr_360px] gap-6">
        <section>
          <h2 className="text-sm font-semibold text-slate-300 mb-3">Board</h2>
          <KanbanBoard logs={logs} />
        </section>

        <aside>
          <AgentStatus logs={logs} cycles={cycles} />
          <div className="flex items-center justify-between mb-3">
            <h2 className="text-sm font-semibold text-slate-300">Activity</h2>
            <span className="flex items-center gap-1.5 text-xs text-slate-500">
              <span
                className={`w-2 h-2 rounded-full ${connected ? 'bg-emerald-400' : 'bg-slate-500'}`}
              />
              {connected ? 'Live' : 'Offline'}
            </span>
          </div>
          <AgentFeedList logs={logs} />
        </aside>
      </div>
    </main>
  )
}
