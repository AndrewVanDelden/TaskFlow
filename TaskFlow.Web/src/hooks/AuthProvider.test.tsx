import { describe, it, expect, beforeEach } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { AuthProvider } from './AuthProvider'
import { useAuth } from './AuthContext'

// Epic 3 Pre-Merge Code Review, finding 6.4: signOut() had zero coverage anywhere - no test
// clicked "Sign out" or verified localStorage was cleared / isAuthenticated flipped back to false.
describe('AuthProvider', () => {
  beforeEach(() => localStorage.clear())

  const renderAuth = () => renderHook(() => useAuth(), { wrapper: AuthProvider })

  it('signIn stores the token/name and flips isAuthenticated', () => {
    const { result } = renderAuth()

    act(() => result.current.signIn('a.jwt.token', 'Ada'))

    expect(result.current.isAuthenticated).toBe(true)
    expect(result.current.userName).toBe('Ada')
    expect(localStorage.getItem('taskflow_token')).toBe('a.jwt.token')
    expect(localStorage.getItem('taskflow_user')).toBe('Ada')
  })

  it('signOut clears the token/name and flips isAuthenticated back to false', () => {
    const { result } = renderAuth()
    act(() => result.current.signIn('a.jwt.token', 'Ada'))

    act(() => result.current.signOut())

    expect(result.current.isAuthenticated).toBe(false)
    expect(result.current.userName).toBeNull()
    expect(localStorage.getItem('taskflow_token')).toBeNull()
    expect(localStorage.getItem('taskflow_user')).toBeNull()
  })
})
