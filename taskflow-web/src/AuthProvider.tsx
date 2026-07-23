import { useState, type ReactNode } from 'react'
import { getToken, setToken, clearToken } from './api'
import { AuthContext } from './AuthContext'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setTokenState] = useState<string | null>(getToken())
  const [userName, setUserName] = useState<string | null>(
    localStorage.getItem('taskflow_user'),
  )

  const signIn = (newToken: string, name: string) => {
    setToken(newToken)
    localStorage.setItem('taskflow_user', name)
    setTokenState(newToken)
    setUserName(name)
  }

  const signOut = () => {
    clearToken()
    localStorage.removeItem('taskflow_user')
    setTokenState(null)
    setUserName(null)
  }

  return (
    <AuthContext.Provider
      value={{ isAuthenticated: !!token, userName, signIn, signOut }}
    >
      {children}
    </AuthContext.Provider>
  )
}