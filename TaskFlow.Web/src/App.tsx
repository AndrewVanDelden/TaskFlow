import { AuthProvider } from './hooks/AuthProvider'
import { useAuth } from './hooks/AuthContext'
import { Login } from './features/Login'
import { Dashboard } from './features/Dashboard'
import { AgentHubProvider } from './lib/agentHub'

function Shell() {
  const { isAuthenticated } = useAuth()
  // The single SignalR connection lives here, wrapping the whole authenticated app so the
  // agent feed and the board share it.
  return isAuthenticated ? (
    <AgentHubProvider>
      <Dashboard />
    </AgentHubProvider>
  ) : (
    <Login />
  )
}

export default function App() {
  return (
    <AuthProvider>
      <Shell />
    </AuthProvider>
  )
}