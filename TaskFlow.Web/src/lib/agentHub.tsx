import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { HubConnectionBuilder, HubConnectionState, type HubConnection } from '@microsoft/signalr'
import { getToken, BASE_URL } from '../api/client'

export interface AgentHub {
  connection: HubConnection | null
  connected: boolean
}

// Exported so tests can inject a fake connection without standing up a real (or mocked) one.
export const AgentHubContext = createContext<AgentHub>({ connection: null, connected: false })

// One SignalR connection for the whole app. Feature hooks (useAgentFeed, useBoardTasks) subscribe
// to it rather than each opening their own: one negotiate, one auth, many subscribers.
export function AgentHubProvider({ children }: { children: ReactNode }) {
  const [connection, setConnection] = useState<HubConnection | null>(null)
  const [connected, setConnected] = useState(false)

  useEffect(() => {
    const conn = new HubConnectionBuilder()
      .withUrl(`${BASE_URL}/hubs/agents`, {
        accessTokenFactory: () => getToken() ?? '',
      })
      .withAutomaticReconnect()
      .build()

    conn.onreconnected(() => setConnected(true))
    conn.onclose(() => setConnected(false))

    conn
      .start()
      .then(() => setConnected(true))
      .catch(() => setConnected(false))

    setConnection(conn)

    return () => {
      if (conn.state !== HubConnectionState.Disconnected) {
        conn.stop()
      }
    }
  }, [])

  return (
    <AgentHubContext.Provider value={{ connection, connected }}>
      {children}
    </AgentHubContext.Provider>
  )
}

export function useAgentHub(): AgentHub {
  return useContext(AgentHubContext)
}
