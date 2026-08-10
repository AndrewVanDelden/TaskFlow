import { describe, it, expect } from 'vitest'
import { saveResumeContext } from './jobApplications'

describe('saveResumeContext', () => {
  it('posts the ingestion session id and content, and returns the boolean result', async () => {
    const result = await saveResumeContext('11111111-1111-1111-1111-111111111111', 'My resume text')

    expect(result).toBe(true)
  })
})
