import { AuthProvider } from './hooks/AuthProvider'
import { useAuth } from './hooks/AuthContext'
import { Login } from './features/Login'
import { Dashboard } from './features/Dashboard'

function Shell() {
  const { isAuthenticated } = useAuth()
  return isAuthenticated ? <Dashboard /> : <Login />
}

export default function App() {
  return (
    <AuthProvider>
      <Shell />
    </AuthProvider>
  )
}