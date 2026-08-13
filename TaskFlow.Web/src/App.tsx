import { useState } from 'react'
import { Routes, Route, Navigate, Outlet } from 'react-router-dom'
import { AuthProvider } from './hooks/AuthProvider'
import { useAuth } from './hooks/AuthContext'
import { AgentHubProvider } from './lib/agentHub'
import { NavBar } from './components/NavBar'
import { Login } from './features/Login'
import { Dashboard } from './features/Dashboard'
import { IngestDocument } from './features/IngestDocument'

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

// Guards the authenticated area and provides the shared shell: one SignalR connection and the nav bar
// around every authenticated screen. Board and Ingest render into the <Outlet/>.
function ProtectedLayout() {
  const { isAuthenticated } = useAuth()
  if (!isAuthenticated) return <Navigate to="/login" replace />

  return (
    <AgentHubProvider>
      <div className="min-h-screen bg-slate-950 text-white">
        <NavBar />
        <Outlet />
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
        </Route>
        <Route path="*" element={<Navigate to="/board" replace />} />
      </Routes>
    </AuthProvider>
  )
}
