import { AuthProvider } from './AuthProvider'
import { useAuth } from './AuthContext'
import { Login } from './Login'
import { Dashboard } from './Dashboard'

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