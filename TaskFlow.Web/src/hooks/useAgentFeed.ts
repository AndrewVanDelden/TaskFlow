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

// User report (2026-08-24): the Task Prioritizer / Stale Task Detector status badges never
// visibly flip to "Running" - a 'started' AgentCycle event immediately followed by 'completed'
// (real cycles can do this in a few ms) overwrote the same cycles[agentName] entry before the
// browser ever painted the intermediate state. Holding 'started' visible for at least this long
// regardless of how fast the real cycle finished makes it perceptible.
const MIN_VISIBLE_CYCLE_MS = 500

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

    // Per-agent 'started' timestamps and pending timeouts for the minimum-visible-duration hold
    // below (see MIN_VISIBLE_CYCLE_MS) - local to this effect run, cleared on cleanup.
    const startedAt: Record<string, number> = {}
    const pendingTimeouts: ReturnType<typeof setTimeout>[] = []

    const onCycle = (evt: CycleEvent) => {
      if (evt.phase === 'started') {
        startedAt[evt.agentName] = Date.now()
        setCycles((prev) => ({ ...prev, [evt.agentName]: evt }))
        return
      }

      const elapsedSinceStarted = Date.now() - (startedAt[evt.agentName] ?? 0)
      const delay = Math.max(0, MIN_VISIBLE_CYCLE_MS - elapsedSinceStarted)
      pendingTimeouts.push(setTimeout(() => setCycles((prev) => ({ ...prev, [evt.agentName]: evt })), delay))
    }

    connection.on(HubEvents.AgentAction, onAction)
    connection.on(HubEvents.AgentCycle, onCycle)

    return () => {
      connection.off(HubEvents.AgentAction, onAction)
      connection.off(HubEvents.AgentCycle, onCycle)
      pendingTimeouts.forEach(clearTimeout)
    }
  }, [connection, maxItems])

  return { logs, cycles, connected }
}
