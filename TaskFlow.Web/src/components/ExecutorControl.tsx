import { useEffect, useState } from 'react'
import { getExecutorState, enableExecutor, disableExecutor } from '../api/executor'

// Enable/pause the autonomous executor. State comes from the server (the runtime kill switch), so
// the UI reflects the real switch rather than a guess.
export function ExecutorControl() {
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

  // Whole card tints faded green when enabled, faded red when paused (neutral while loading).
  const shell =
    enabled === null
      ? 'bg-slate-900/60 border-slate-800'
      : enabled
      ? 'bg-emerald-500/10 border-emerald-500/40'
      : 'bg-red-500/10 border-red-500/40'

  const pill =
    enabled === null
      ? 'bg-slate-800 text-slate-400 border-slate-700'
      : enabled
      ? 'bg-emerald-500/15 text-emerald-200 border-emerald-500/30'
      : 'bg-red-500/15 text-red-200 border-red-500/30'

  return (
    <div className={`border rounded-xl p-4 mb-6 transition-colors ${shell}`}>
      <div className="flex items-center justify-between gap-3">
        <div>
          <div className="flex items-center gap-2">
            <h3 className="text-sm font-semibold text-white">Autonomous executor</h3>
            <span className={`text-[11px] px-2 py-0.5 rounded-full border ${pill}`}>
              {enabled === null ? '…' : enabled ? 'Enabled' : 'Paused'}
            </span>
          </div>
          <p className="text-xs text-slate-400 mt-1">Claims To Do tasks and works them to Review</p>
        </div>

        <button
          onClick={toggle}
          disabled={enabled === null || busy}
          className="text-xs font-semibold px-3 py-1.5 rounded border border-slate-600 bg-slate-800/80 hover:border-slate-500 text-slate-100 disabled:opacity-50"
        >
          {enabled === null ? '…' : enabled ? 'Pause' : 'Enable'}
        </button>
      </div>
    </div>
  )
}
