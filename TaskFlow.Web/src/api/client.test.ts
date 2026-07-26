import { describe, it, expect } from 'vitest'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import { setToken, getToken } from './client'
import { getTasks } from './tasks'

describe('401 handling', () => {
  it('clears the stored token when a protected call returns 401', async () => {
    setToken('stale.token')

    // Override just for this test: make /api/Tasks answer 401.
    server.use(
      http.get('*/api/Tasks', () => new HttpResponse(null, { status: 401 })),
    )

    await expect(getTasks()).rejects.toThrow() // the call fails
    expect(getToken()).toBeNull() // and the token was cleared
  })
})
