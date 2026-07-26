import { describe, it, expect } from 'vitest'
import { login } from './auth'

describe('login', () => {
  it('returns the token from the API', async () => {
    // The default handler in src/test/handlers.ts answers this; the real login() runs.
    const res = await login('ada@x.dev', 'pw')
    expect(res.token).toBe('fake.jwt.token')
  })
})
