import { useExecutorControl } from '../hooks/useExecutorControl'
import { usePrefersReducedMotion } from '../hooks/usePrefersReducedMotion'
import { Button } from './ui/Button'
import { textNeutral500, bgAccent400 } from '../lib/tokens'

// Enable/pause the autonomous executor. State comes from the server (the runtime kill switch), so
// the UI reflects the real switch rather than a guess.
export function ExecutorControl() {
  const { enabled, busy, toggle } = useExecutorControl()
  const prefersReducedMotion = usePrefersReducedMotion()

  const dotColor = enabled === true ? bgAccent400 : 'bg-white/20'
  // Only pulse while actually running, and never against the user's reduced-motion preference.
  const pulse = enabled === true && !prefersReducedMotion ? 'animate-pulse' : ''

  const summary =
    enabled === null ? 'Loading executor status…' : enabled ? 'Executor running' : 'Executor paused'

  return (
    <div className="flex items-center gap-3 mb-6">
      <span data-testid="executor-status-dot" className={`h-2 w-2 rounded-full ${dotColor} ${pulse}`} />
      <p className={`text-sm ${textNeutral500} flex-1`}>{summary}</p>
      <Button variant="ghost" onClick={toggle} disabled={enabled === null || busy}>
        {enabled === null ? '…' : enabled ? 'Pause' : 'Enable'}
      </Button>
    </div>
  )
}
