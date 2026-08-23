import { clearToken } from '../api/client'

const BOOT_ID_KEY = 'taskflow_boot_id'
const USER_KEY = 'taskflow_user'

// Dev-only: forces a fresh sign-in every time the dev server restarts (.\run), even if a valid
// token is still in localStorage from a previous run. vite.config.ts bakes a new __APP_BOOT_ID__
// into the served bundle on every dev-server boot; every page refresh WITHIN that same boot shares
// the identical id (so ordinary dev work - refreshing while iterating - never force-logs-out), but
// a fresh `.\run` produces a new one, detected here by comparing against the last-seen id persisted
// in localStorage. isDev guards this from ever firing in a real production build, where one fixed
// boot id lives for the whole build's lifetime and real users must stay signed in.
export function clearAuthOnFreshDevServerBoot(
  bootId: string = __APP_BOOT_ID__,
  isDev: boolean = import.meta.env.DEV,
): void {
  if (!isDev) return
  if (localStorage.getItem(BOOT_ID_KEY) === bootId) return

  clearToken()
  localStorage.removeItem(USER_KEY)
  localStorage.setItem(BOOT_ID_KEY, bootId)
}
