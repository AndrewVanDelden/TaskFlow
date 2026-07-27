import { describe, it, expect } from 'vitest'
import { parseDocument } from './ingestion'

describe('parseDocument', () => {
  it('returns the drafts from the API', async () => {
    const drafts = await parseDocument('# doc')

    expect(drafts).toHaveLength(1)
    expect(drafts[0].title).toBe('Draft from server')
  })
})
