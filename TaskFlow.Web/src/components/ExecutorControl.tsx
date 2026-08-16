import { useExecutorControl } from '../hooks/useExecutorControl'
import { usePrefersReducedMotion } from '../hooks/usePrefersReducedMotion'
import { Button } from './ui/Button'
import { textNeutral500 } from '../lib/tokens'

// Enable/pause the autonomous executor. State comes from the server (the runtime kill switch), so
// the UI reflects the real switch rather than a guess.
//
// Dot color deliberately matches this same Board screen's own existing running/idle vocabulary
// (AgentStatus's "Running" pill, Dashboard's "Live" connection dot) rather than the epic's general
// accent-purple token set: those two neighbors were real, glanceable precedent that a solid emerald
// dot means "on" here, and neither uses red for "off" - paused matches AgentStatus's "Idle" dot (a
// solid, clearly-visible neutral) instead of introducing a color with no adjacent precedent.
export function ExecutorControl() {
  const { enabled, busy, toggle } = useExecutorControl()
  const prefersReducedMotion = usePrefersReducedMotion()

  const dotColor = enabled === true ? 'bg-emerald-400' : 'bg-slate-500'
  // Only pulse while actually running, and never against the user's reduced-motion preference.
  const pulse = enabled === true && !prefersReducedMotion ? 'animate-pulse' : ''

  const summary =
    enabled === null ? 'Loading executor status…' : enabled ? 'Executor running' : 'Executor paused'

  return (
    // Rendered full-width in Dashboard's <main>, above the two-column split - capped here so the
    // status text and its button stay a compact, self-contained group instead of stretching the
    // button to the far edge of the whole page.
    <div data-testid="executor-control-row" className="flex items-center gap-3 mb-6 max-w-sm">
      <span data-testid="executor-status-dot" className={`h-2 w-2 rounded-full ${dotColor} ${pulse}`} />
      <p className={`text-sm ${textNeutral500} flex-1`}>{summary}</p>
      <Button variant="ghost" onClick={toggle} disabled={enabled === null || busy}>
        {enabled === null ? '…' : enabled ? 'Pause' : 'Enable'}
      </Button>
    </div>
  )
}
