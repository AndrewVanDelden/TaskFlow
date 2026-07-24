import type { AgentLog } from './types'
import type { CycleEvent } from './useAgentFeed'

const AGENTS = [
  { name: 'TaskPrioritizer', label: 'Task Prioritizer', blurb: 'Re-ranks priorities' },
  { name: 'StaleTaskDetector', label: 'Stale Task Detector', blurb: 'Finds abandoned work' },
]

export function AgentStatus({
  logs,
  cycles,
}: {
  logs: AgentLog[]
  cycles: Record<string, CycleEvent>
}) {
  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 mb-4">
      {AGENTS.map((agent) => {
        const cycle = cycles[agent.name]
        const running = cycle?.phase === 'started'
        const agentLogs = logs.filter((l) => l.agentName === agent.name)
        const lastLog = agentLogs[0]

        return (
          <div
            key={agent.name}
            className="bg-slate-900/60 border border-slate-800 rounded-xl p-4"
          >
            <div className="flex items-center justify-between mb-2">
              <h3 className="text-sm font-semibold text-white">{agent.label}</h3>
              <span
                className={`flex items-center gap-1.5 text-[11px] px-2 py-0.5 rounded-full border ${
                  running
                    ? 'bg-emerald-500/15 text-emerald-300 border-emerald-500/30'
                    : 'bg-slate-500/15 text-slate-400 border-slate-500/30'
                }`}
              >
                <span
                  className={`w-1.5 h-1.5 rounded-full ${
                    running ? 'bg-emerald-400 animate-pulse' : 'bg-slate-500'
                  }`}
                />
                {running ? 'Running' : 'Idle'}
              </span>
            </div>

            <p className="text-xs text-slate-500 mb-3">{agent.blurb}</p>

            <div className="grid grid-cols-2 gap-2 text-[11px]">
              <div>
                <div className="text-slate-600">Actions logged</div>
                <div className="text-slate-300 font-medium">{agentLogs.length}</div>
              </div>
              <div>
                <div className="text-slate-600">Last activity</div>
                <div className="text-slate-300 font-medium">
                  {lastLog
                    ? new Date(lastLog.createdAt).toLocaleTimeString()
                    : 'None yet'}
                </div>
              </div>
            </div>
          </div>
        )
      })}
    </div>
  )
}