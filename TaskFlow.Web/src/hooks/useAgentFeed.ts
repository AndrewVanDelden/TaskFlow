import { useEffect, useState } from 'react'
import type { AgentLog } from '../types'
import { getAgentLogs } from '../api/agentLogs'
import { HubEvents } from '../lib/hubEvents'
import { useAgentHub } from '../lib/AgentHubContext'

export interface CycleEvent {
  agentName: string
  phase: string
  at: string
}

// Consumes the app-wide connection from useAgentHub and subscribes to the agent events.
// It no longer owns the connection (that moved to AgentHubProvider), so the board can share it.
export function useAgentFeed(maxItems = 50) {
  const [logs, setLogs] = useState<AgentLog[]>([])
  const [cycles, setCycles] = useState<Record<string, CycleEvent>>({})
  const { connection, connected } = useAgentHub()

  // Seed with history so the feed is not empty on first load.
  useEffect(() => {
    getAgentLogs(maxItems).then(setLogs).catch(() => {})
  }, [maxItems])

  // Subscribe to live agent events on the shared connection.
  useEffect(() => {
    if (!connection) return

    const onAction = (log: AgentLog) => setLogs((prev) => [log, ...prev].slice(0, maxItems))
    const onCycle = (evt: CycleEvent) => setCycles((prev) => ({ ...prev, [evt.agentName]: evt }))

    connection.on(HubEvents.AgentAction, onAction)
    connection.on(HubEvents.AgentCycle, onCycle)

    return () => {
      connection.off(HubEvents.AgentAction, onAction)
      connection.off(HubEvents.AgentCycle, onCycle)
    }
  }, [connection, maxItems])

  return { logs, cycles, connected }
}
