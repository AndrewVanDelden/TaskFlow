import type { AgentLog } from '../types'
import { formatRelativeTime } from '../lib/formatting'
import { textNeutral400, textNeutral500 } from '../lib/tokens'

export function AgentFeedList({ logs }: { logs: AgentLog[] }) {
  if (logs.length === 0) {
    return <p className={`text-xs text-center py-8 ${textNeutral500}`}>No agent activity yet.</p>
  }

  return (
    <ul>
      {logs.map((log) => (
        <li key={`${log.id}-${log.createdAt}`} className="border-b border-white/10 py-2.5 text-xs">
          <span className={textNeutral400}>{log.agentName}</span>{' '}
          {log.taskId !== null && <span className={textNeutral500}>Task #{log.taskId}</span>}{' '}
          <span>{log.details || log.action}</span>{' '}
          <span className={`${textNeutral500} float-right`}>{formatRelativeTime(log.createdAt)}</span>
        </li>
      ))}
    </ul>
  )
}
