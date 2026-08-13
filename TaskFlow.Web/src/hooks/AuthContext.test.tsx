import { describe, it, expect } from 'vitest'
import { renderHook } from '@testing-library/react'
import { useAuth } from './AuthContext'

// Epic 3 Pre-Merge Code Review, finding 6.4: the "must be used inside AuthProvider" throw branch
// had zero coverage.
describe('useAuth', () => {
  it('throws when called outside an AuthProvider', () => {
    expect(() => renderHook(() => useAuth())).toThrow('useAuth must be used inside AuthProvider')
  })
})
