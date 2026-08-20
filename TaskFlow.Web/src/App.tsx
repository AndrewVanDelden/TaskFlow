import { useState } from 'react'
import { Routes, Route, Navigate, Outlet } from 'react-router-dom'
import { AuthProvider } from './hooks/AuthProvider'
import { useAuth } from './hooks/AuthContext'
import { AgentHubProvider } from './lib/agentHub'
import { SideBar } from './components/SideBar'
import { Login } from './features/Login'
import { Dashboard } from './features/Dashboard'
import { IngestDocument } from './features/IngestDocument'
import { Activity } from './features/Activity'
import { Archive } from './features/Archive'
import { bgPage, textPrimary } from './lib/tokens'

// Keep authenticated users off the login screen.
function LoginRoute() {
  const { isAuthenticated } = useAuth()
  return isAuthenticated ? <Navigate to="/board" replace /> : <Login />
}

// Bare /ingest (the nav link's target) has no session id yet - generate one and redirect to the
// real, id-bearing route. This is what makes the session id survive an unmount/remount: it now
// lives in the URL, not component state (PR #40 review finding #7).
function IngestRedirect() {
  const [sessionId] = useState(() => crypto.randomUUID())
  return <Navigate to={`/ingest/${sessionId}`} replace />
}

// Guards the authenticated area and provides the shared shell: one SignalR connection and the side
// bar around every authenticated screen. Board and Ingest render into the <Outlet/>.
function ProtectedLayout() {
  const { isAuthenticated } = useAuth()
  if (!isAuthenticated) return <Navigate to="/login" replace />

  return (
    <AgentHubProvider>
      <div className={`h-screen flex ${bgPage} ${textPrimary}`}>
        <SideBar />
        <div className="flex-1 overflow-y-auto">
          <Outlet />
        </div>
      </div>
    </AgentHubProvider>
  )
}

export default function App() {
  return (
    <AuthProvider>
      <Routes>
        <Route path="/login" element={<LoginRoute />} />
        <Route element={<ProtectedLayout />}>
          <Route path="/board" element={<Dashboard />} />
          <Route path="/ingest" element={<IngestRedirect />} />
          <Route path="/ingest/:sessionId" element={<IngestDocument />} />
          <Route path="/activity" element={<Activity />} />
          <Route path="/archive" element={<Archive />} />
        </Route>
        <Route path="*" element={<Navigate to="/board" replace />} />
      </Routes>
    </AuthProvider>
  )
}
