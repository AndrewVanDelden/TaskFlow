import { useEffect, useState } from 'react'
import { getExecutorState, enableExecutor, disableExecutor } from '../api/executor'

// Owns the executor toggle's state: the enabled flag comes from the server (the runtime kill
// switch) on mount, then flips via enable/disable, mirroring useApplicationReview/
// useExportDownload's shape. Both the initial fetch and the toggle keep the current state on
// failure rather than guessing, so a transient network error never flips the UI to a state the
// server didn't actually reach.
export function useExecutorControl() {
  const [enabled, setEnabled] = useState<boolean | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    getExecutorState()
      .then((s) => setEnabled(s.enabled))
      .catch(() => {})
  }, [])

  const toggle = async () => {
    if (enabled === null || busy) return
    setBusy(true)
    try {
      const next = enabled ? await disableExecutor() : await enableExecutor()
      setEnabled(next.enabled)
    } catch {
      // Keep the current state on failure.
    } finally {
      setBusy(false)
    }
  }

  return { enabled, busy, toggle }
}
