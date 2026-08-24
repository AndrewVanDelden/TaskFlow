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
    //
    // PR #71 review finding (Antigravity/Gemini, independently confirmed by a second manual
    // review): pendingTimeouts was a flat array, not keyed per agent, so a fast second cycle's
    // immediate 'started' apply could later be clobbered by the *first* cycle's still-pending
    // delayed 'completed' timeout firing on its own original schedule. Keying by agentName and
    // clearing any existing timeout for that agent whenever a new event arrives - whether it's a
    // fresh 'started' or a fresh non-started - means only the most recent event for an agent can
    // ever apply, and the map never grows past one entry per agent.
    const startedAt: Record<string, number> = {}
    const pendingTimeouts: Record<string, ReturnType<typeof setTimeout>> = {}

    const onCycle = (evt: CycleEvent) => {
      const existingTimeout = pendingTimeouts[evt.agentName]
      if (existingTimeout) {
        clearTimeout(existingTimeout)
        delete pendingTimeouts[evt.agentName]
      }

      if (evt.phase === 'started') {
        startedAt[evt.agentName] = Date.now()
        setCycles((prev) => ({ ...prev, [evt.agentName]: evt }))
        return
      }

      const elapsedSinceStarted = Date.now() - (startedAt[evt.agentName] ?? 0)
      const delay = Math.max(0, MIN_VISIBLE_CYCLE_MS - elapsedSinceStarted)
      pendingTimeouts[evt.agentName] = setTimeout(() => {
        delete pendingTimeouts[evt.agentName]
        setCycles((prev) => ({ ...prev, [evt.agentName]: evt }))
      }, delay)
    }

    connection.on(HubEvents.AgentAction, onAction)
    connection.on(HubEvents.AgentCycle, onCycle)

    return () => {
      connection.off(HubEvents.AgentAction, onAction)
      connection.off(HubEvents.AgentCycle, onCycle)
      Object.values(pendingTimeouts).forEach(clearTimeout)
    }
  }, [connection, maxItems])

  return { logs, cycles, connected }
}
