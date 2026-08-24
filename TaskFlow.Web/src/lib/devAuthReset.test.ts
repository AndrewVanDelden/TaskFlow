import { describe, it, expect, beforeEach } from 'vitest'
import { clearAuthOnFreshDevServerBoot } from './devAuthReset'

// User report (2026-08-22): starting `.\run` landed on the Board, not sign-in, because a valid
// token was still in localStorage from a prior session. These prove the boot-id comparison that
// fixes it: a fresh dev-server boot (a new id baked into the bundle) clears the stored session, but
// an ordinary page refresh within the same dev-server lifetime (the same id) does not.
describe('clearAuthOnFreshDevServerBoot', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('clears the stored token and user when the boot id differs from the last-seen one', () => {
    localStorage.setItem('taskflow_token', 'stale-token')
    localStorage.setItem('taskflow_user', 'Stale User')
    localStorage.setItem('taskflow_boot_id', 'old-boot-id')

    clearAuthOnFreshDevServerBoot('new-boot-id', true)

    expect(localStorage.getItem('taskflow_token')).toBeNull()
    expect(localStorage.getItem('taskflow_user')).toBeNull()
  })

  it('persists the new boot id after clearing, so a later call with the same id is a no-op', () => {
    localStorage.setItem('taskflow_boot_id', 'old-boot-id')

    clearAuthOnFreshDevServerBoot('new-boot-id', true)

    expect(localStorage.getItem('taskflow_boot_id')).toBe('new-boot-id')
  })

  it('does not clear anything when the boot id matches the last-seen one (a page refresh, not a fresh .\\run)', () => {
    localStorage.setItem('taskflow_token', 'valid-token')
    localStorage.setItem('taskflow_user', 'Current User')
    localStorage.setItem('taskflow_boot_id', 'same-boot-id')

    clearAuthOnFreshDevServerBoot('same-boot-id', true)

    expect(localStorage.getItem('taskflow_token')).toBe('valid-token')
    expect(localStorage.getItem('taskflow_user')).toBe('Current User')
  })

  it('does nothing outside dev mode, even with a mismatched boot id (production must never force-logout real users)', () => {
    localStorage.setItem('taskflow_token', 'valid-token')
    localStorage.setItem('taskflow_boot_id', 'old-boot-id')

    clearAuthOnFreshDevServerBoot('new-boot-id', false)

    expect(localStorage.getItem('taskflow_token')).toBe('valid-token')
  })

  it('treats no last-seen boot id (first-ever load) as a fresh boot and clears', () => {
    localStorage.setItem('taskflow_token', 'stale-token')

    clearAuthOnFreshDevServerBoot('some-boot-id', true)

    expect(localStorage.getItem('taskflow_token')).toBeNull()
  })
})
