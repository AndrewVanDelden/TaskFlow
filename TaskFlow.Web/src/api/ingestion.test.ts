import { describe, it, expect } from 'vitest'
import { parseDocument, commitDrafts } from './ingestion'

describe('parseDocument', () => {
  it('returns the drafts from the API', async () => {
    const drafts = await parseDocument('# doc')

    expect(drafts).toHaveLength(1)
    expect(drafts[0].title).toBe('Draft from server')
  })
})

describe('commitDrafts', () => {
  it('posts the drafts and returns the committed count', async () => {
    const count = await commitDrafts('spec.md', [
      { title: 'Wire auth', description: null, kind: 'Generic', section: 'Backend' },
    ])

    expect(count).toBe(1)
  })
})
