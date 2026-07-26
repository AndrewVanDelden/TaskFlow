// Shared Tailwind class maps. Single source of truth for badge colors so no component
// hard-codes its own copy. The action keys mirror TaskFlow.Api/Agents/AgentConstants.cs.

export const priorityStyles: Record<string, string> = {
  High: 'bg-red-500/15 text-red-300 border-red-500/30',
  Medium: 'bg-amber-500/15 text-amber-300 border-amber-500/30',
  Low: 'bg-slate-500/15 text-slate-300 border-slate-500/30',
}

export const actionStyles: Record<string, string> = {
  Escalated: 'bg-red-500/15 text-red-300 border-red-500/30',
  Reassigned: 'bg-blue-500/15 text-blue-300 border-blue-500/30',
  FlaggedForReview: 'bg-amber-500/15 text-amber-300 border-amber-500/30',
  PriorityUpdated: 'bg-emerald-500/15 text-emerald-300 border-emerald-500/30',
  PrioritiesUpdated: 'bg-emerald-500/15 text-emerald-300 border-emerald-500/30',
  NoChangesNeeded: 'bg-slate-500/15 text-slate-400 border-slate-500/30',
  NoActionNeeded: 'bg-slate-500/15 text-slate-400 border-slate-500/30',
  CycleActions: 'bg-violet-500/15 text-violet-300 border-violet-500/30',
}

export const neutralStyle = 'bg-slate-500/15 text-slate-400 border-slate-500/30'
