import { describe, it, expect } from 'vitest'
import { formatDate, formatTime, formatRelativeTime } from './formatting'

// Epic 3 Pre-Merge Code Review, finding 6.3: formatDate/formatTime had no direct test.
describe('formatting', () => {
  it('formatDate renders a locale date string for a valid ISO timestamp', () => {
    const result = formatDate('2026-08-13T10:00:00Z')

    expect(result).toBe(new Date('2026-08-13T10:00:00Z').toLocaleDateString())
    expect(result.length).toBeGreaterThan(0)
  })

  it('formatTime renders a locale time string for a valid ISO timestamp', () => {
    const result = formatTime('2026-08-13T10:00:00Z')

    expect(result).toBe(new Date('2026-08-13T10:00:00Z').toLocaleTimeString())
    expect(result.length).toBeGreaterThan(0)
  })
})

describe('formatRelativeTime', () => {
  const now = new Date('2026-08-14T12:00:00Z')

  it('returns "just now" for a timestamp less than 60 seconds ago', () => {
    const iso = new Date(now.getTime() - 30 * 1000).toISOString()

    expect(formatRelativeTime(iso, now)).toBe('just now')
  })

  it('returns minutes ago for a timestamp minutes in the past', () => {
    const iso = new Date(now.getTime() - 5 * 60 * 1000).toISOString()

    expect(formatRelativeTime(iso, now)).toBe('5m ago')
  })

  it('returns hours ago for a timestamp hours in the past', () => {
    const iso = new Date(now.getTime() - 3 * 60 * 60 * 1000).toISOString()

    expect(formatRelativeTime(iso, now)).toBe('3h ago')
  })

  it('returns days ago for a timestamp days in the past', () => {
    const iso = new Date(now.getTime() - 2 * 24 * 60 * 60 * 1000).toISOString()

    expect(formatRelativeTime(iso, now)).toBe('2d ago')
  })
})
