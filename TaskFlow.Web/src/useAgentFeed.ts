import { useEffect, useRef, useState } from 'react'
import { HubConnectionBuilder, HubConnectionState, type HubConnection } from '@microsoft/signalr'
import type { AgentLog } from './types'
import { getToken, getAgentLogs } from './api'

const BASE_URL = import.meta.env.VITE_API_BASE_URL as string

export interface CycleEvent {
  agentName: string
  phase: string
  at: string
}

export function useAgentFeed(maxItems = 50) {
  const [logs, setLogs] = useState<AgentLog[]>([])
  const [cycles, setCycles] = useState<Record<string, CycleEvent>>({})
  const [connected, setConnected] = useState(false)
  const connectionRef = useRef<HubConnection | null>(null)

  useEffect(() => {
    // Seed with history so the feed is not empty on first load.
    getAgentLogs(maxItems).then(setLogs).catch(() => {})

    const connection = new HubConnectionBuilder()
      .withUrl(`${BASE_URL}/hubs/agents`, {
        accessTokenFactory: () => getToken() ?? '',
      })
      .withAutomaticReconnect()
      .build()

    connection.on('AgentAction', (log: AgentLog) => {
      setLogs((prev) => [log, ...prev].slice(0, maxItems))
    })

    connection.on('AgentCycle', (evt: CycleEvent) => {
      setCycles((prev) => ({ ...prev, [evt.agentName]: evt }))
    })

    connection.onreconnected(() => setConnected(true))
    connection.onclose(() => setConnected(false))

    connection
      .start()
      .then(() => setConnected(true))
      .catch(() => setConnected(false))

    connectionRef.current = connection

    // Cleanup: React runs this when the component unmounts.
    return () => {
      if (connection.state !== HubConnectionState.Disconnected) {
        connection.stop()
      }
    }
  }, [maxItems])

  return { logs, cycles, connected }
}