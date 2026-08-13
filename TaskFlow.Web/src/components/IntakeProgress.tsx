import { Link } from 'react-router-dom'
import type { AgentLog } from '../types'
import { taskStage, type TaskStage } from '../lib/board'

const STAGE_LABEL: Record<TaskStage, string> = {
  pending: 'Waiting to start',
  'in-progress': 'In progress…',
  saved: 'Saved, ready for review',
  'rolled-back': 'Retrying…',
}

function ProgressRow({ label, stage }: { label: string; stage: TaskStage }) {
  return (
    <div className="flex items-center justify-between text-sm py-1">
      <span className="text-slate-300">{label}</span>
      <span className="text-xs text-slate-400" aria-busy={stage === 'in-progress'}>
        {STAGE_LABEL[stage]}
      </span>
    </div>
  )
}

// Per-item live progress for the two Epic 3 tailoring tasks (resume + cover letter), rendered
// within useIntakeFlow's 'building' stage (T6.3). Purely additive/presentational: given the two
// task ids useIntakeFlow already exposes plus the shared AgentLog feed (useAgentFeed, the same one
// KanbanBoard already consumes), it derives each item's stage via taskStage and its own "both
// saved" ready condition - useIntakeFlow never needs to know this happened.
export function IntakeProgress({
  logs,
  resumeTaskId,
  coverLetterTaskId,
}: {
  logs: AgentLog[]
  resumeTaskId: number
  coverLetterTaskId: number
}) {
  const resumeStage = taskStage(logs, resumeTaskId)
  const coverLetterStage = taskStage(logs, coverLetterTaskId)
  const bothSaved = resumeStage === 'saved' && coverLetterStage === 'saved'

  return (
    <div className="mt-4 rounded bg-slate-900/60 border border-slate-800 p-3">
      <p className="text-sm text-slate-300 mb-2">
        Your tailored resume and cover letter are being generated.
      </p>
      <ProgressRow label="Tailored resume" stage={resumeStage} />
      <ProgressRow label="Cover letter" stage={coverLetterStage} />
      {bothSaved && (
        <p className="mt-3 text-sm text-emerald-400">
          Ready for review — <Link to="/board" className="underline">view it on the board</Link>.
        </p>
      )}
    </div>
  )
}
