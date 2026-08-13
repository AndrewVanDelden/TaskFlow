import { describe, it, expect } from 'vitest'
import { formatDate, formatTime } from './formatting'

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
