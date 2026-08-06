import { describe, it, expect } from 'vitest'
import { renderHook, act, waitFor } from '@testing-library/react'
import { useIngestion } from './useIngestion'

describe('useIngestion', () => {
  it('submits content and exposes the returned drafts', async () => {
    const { result } = renderHook(() => useIngestion())

    await act(async () => {
      await result.current.submit('# doc')
    })

    await waitFor(() => expect(result.current.drafts).toHaveLength(1))
    expect(result.current.drafts[0].title).toBe('Draft from server')
  })
})
