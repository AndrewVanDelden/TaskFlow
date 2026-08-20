import { useEffect, useState, type ReactNode } from 'react'
import { HubConnectionBuilder, HubConnectionState, type HubConnection } from '@microsoft/signalr'
import { getToken, BASE_URL } from '../api/client'
import { AgentHubContext } from './AgentHubContext'

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

    // Exposing the connection this same effect just built is the entire point: useAgentFeed/
    // useBoardTasks depend on `connection`'s identity to know when to subscribe, so it has to be
    // real state, not a ref - there's no render-time equivalent for "an external resource was
    // just constructed."
    // eslint-disable-next-line react-hooks/set-state-in-effect
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
