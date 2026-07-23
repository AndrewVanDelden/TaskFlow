import { useAuth } from './AuthContext'
import { useAgentFeed } from './useAgentFeed'
import { KanbanBoard } from './KanbanBoard'
import { AgentFeed } from './AgentFeed'
import { AgentStatus } from './AgentStatus'

export function Dashboard() {
  const { userName, signOut } = useAuth()
  const { logs, cycles, connected } = useAgentFeed()

  return (
    <div className="min-h-screen bg-slate-950 text-white">
      <header className="border-b border-slate-800 px-6 py-3 flex items-center justify-between">
        <div>
          <h1 className="text-lg font-bold">TaskFlow</h1>
          <p className="text-xs text-slate-500">Autonomous agent workspace</p>
        </div>
        <div className="flex items-center gap-3">
          <span className="text-sm text-slate-400">{userName}</span>
          <button
            onClick={signOut}
            className="text-xs border border-slate-700 hover:border-slate-600 px-3 py-1.5 rounded"
          >
            Sign out
          </button>
        </div>
      </header>

      <main className="p-6 grid grid-cols-1 xl:grid-cols-[1fr_360px] gap-6">
        <section>
          <h2 className="text-sm font-semibold text-slate-300 mb-3">Board</h2>
          <KanbanBoard refreshKey={logs.length} />
        </section>

        <aside>
          <AgentStatus logs={logs} cycles={cycles} />
          <AgentFeed logs={logs} connected={connected} />
        </aside>
      </main>
    </div>
  )
}