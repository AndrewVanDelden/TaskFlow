import { createContext, useContext } from 'react'
import type { HubConnection } from '@microsoft/signalr'

export interface AgentHub {
  connection: HubConnection | null
  connected: boolean
}

// Exported so tests can inject a fake connection without standing up a real (or mocked) one.
export const AgentHubContext = createContext<AgentHub>({ connection: null, connected: false })

export function useAgentHub(): AgentHub {
  return useContext(AgentHubContext)
}
