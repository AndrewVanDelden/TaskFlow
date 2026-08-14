// Shared Tailwind class maps. Single source of truth for badge colors so no component
// hard-codes its own copy.

// Epic 3.1: priorityStyles (TaskCardView's old filled pill) and actionStyles (AgentFeed's old
// badged rows, component retired) were both red/amber/green status coloring the Nocturne design
// replaces with quiet text - retired here once nothing imported them anymore.
export const neutralStyle = 'bg-slate-500/15 text-slate-400 border-slate-500/30'
