import { describe, it, expect, vi } from 'vitest'
import { openTextInNewTab } from './openTextInNewTab'

// User report (2026-08-22): the base resume has no backend PDF export of its own (it's the
// candidate's own pasted text, not an AI-tailored artifact) - opening it in a new tab means writing
// it directly into a freshly opened window, so these prove that path is both correct and safe.
describe('openTextInNewTab', () => {
  it('opens a blank window and writes the content and title into it', () => {
    const writeSpy = vi.fn()
    const closeSpy = vi.fn()
    const fakeWindow = { document: { write: writeSpy, close: closeSpy } } as unknown as Window
    const openSpy = vi.spyOn(window, 'open').mockReturnValue(fakeWindow)

    openTextInNewTab('My resume text', 'Base resume')

    expect(openSpy).toHaveBeenCalledWith('', '_blank')
    expect(writeSpy).toHaveBeenCalledWith(expect.stringContaining('My resume text'))
    expect(writeSpy).toHaveBeenCalledWith(expect.stringContaining('Base resume'))
    expect(closeSpy).toHaveBeenCalledOnce()

    openSpy.mockRestore()
  })

  // The content is written via document.write, which interprets HTML - unescaped content would let
  // embedded markup (or a real XSS payload) execute in the new tab.
  it('escapes HTML-special characters so embedded markup cannot execute', () => {
    const writeSpy = vi.fn()
    const fakeWindow = { document: { write: writeSpy, close: vi.fn() } } as unknown as Window
    const openSpy = vi.spyOn(window, 'open').mockReturnValue(fakeWindow)

    openTextInNewTab("<script>alert('xss')</script>", 'Base resume')

    const written = writeSpy.mock.calls[0][0] as string
    expect(written).not.toContain('<script>')
    expect(written).toContain('&lt;script&gt;')

    openSpy.mockRestore()
  })

  it('returns false and does not throw when the browser blocks the window', () => {
    const openSpy = vi.spyOn(window, 'open').mockReturnValue(null)

    expect(() => openTextInNewTab('content', 'title')).not.toThrow()
    expect(openTextInNewTab('content', 'title')).toBe(false)

    openSpy.mockRestore()
  })

  it('returns true when the window opens successfully', () => {
    const fakeWindow = { document: { write: vi.fn(), close: vi.fn() } } as unknown as Window
    const openSpy = vi.spyOn(window, 'open').mockReturnValue(fakeWindow)

    expect(openTextInNewTab('content', 'title')).toBe(true)

    openSpy.mockRestore()
  })
})
