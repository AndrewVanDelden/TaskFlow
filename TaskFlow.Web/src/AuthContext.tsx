import { createContext, useContext } from 'react'

export interface AuthState {
  isAuthenticated: boolean
  userName: string | null
  signIn: (token: string, name: string) => void
  signOut: () => void
}

export const AuthContext = createContext<AuthState | undefined>(undefined)

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}