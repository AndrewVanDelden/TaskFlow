import type { AgentLog } from './types'

const actionStyles: Record<string, string> = {
  Escalated: 'bg-red-500/15 text-red-300 border-red-500/30',
  Reassigned: 'bg-blue-500/15 text-blue-300 border-blue-500/30',
  FlaggedForReview: 'bg-amber-500/15 text-amber-300 border-amber-500/30',
  PriorityUpdated: 'bg-emerald-500/15 text-emerald-300 border-emerald-500/30',
  PrioritiesUpdated: 'bg-emerald-500/15 text-emerald-300 border-emerald-500/30',
  NoChangesNeeded: 'bg-slate-500/15 text-slate-400 border-slate-500/30',
  NoActionNeeded: 'bg-slate-500/15 text-slate-400 border-slate-500/30',
  CycleActions: 'bg-violet-500/15 text-violet-300 border-violet-500/30',
}

export function AgentFeed({
  logs,
  connected,
}: {
  logs: AgentLog[]
  connected: boolean
}) {
  return (
    <div className="bg-slate-900/60 border border-slate-800 rounded-xl p-4">
      <div className="flex items-center justify-between mb-3">
        <h2 className="text-sm font-semibold text-slate-300">Agent Activity</h2>
        <span className="flex items-center gap-1.5 text-xs text-slate-500">
          <span
            className={`w-2 h-2 rounded-full ${
              connected ? 'bg-emerald-400' : 'bg-slate-600'
            }`}
          />
          {connected ? 'Live' : 'Offline'}
        </span>
      </div>

      <div className="space-y-2 max-h-[420px] overflow-y-auto pr-1">
        {logs.length === 0 && (
          <p className="text-xs text-slate-600 text-center py-8">
            No agent activity yet.
          </p>
        )}

        {logs.map((log) => (
          <div
            key={`${log.id}-${log.createdAt}`}
            className="border border-slate-800 rounded-lg p-2.5 bg-slate-900"
          >
            <div className="flex items-center gap-2 mb-1">
              <span
                className={`text-[10px] font-semibold px-2 py-0.5 rounded-full border ${
                  actionStyles[log.action] ?? actionStyles.NoChangesNeeded
                }`}
              >
                {log.action}
              </span>
              <span className="text-[11px] text-slate-500">{log.agentName}</span>
              {log.taskId && (
                <span className="text-[11px] text-slate-600">
                  Task #{log.taskId}
                </span>
              )}
              <span className="text-[11px] text-slate-600 ml-auto">
                {new Date(log.createdAt).toLocaleTimeString()}
              </span>
            </div>
            {log.details && (
              <p className="text-xs text-slate-400 leading-relaxed">
                {log.details}
              </p>
            )}
          </div>
        ))}
      </div>
    </div>
  )
}