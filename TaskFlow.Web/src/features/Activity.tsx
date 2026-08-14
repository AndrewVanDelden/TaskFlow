import { useAgentFeed } from '../hooks/useAgentFeed'
import { AgentFeedList } from '../components/AgentFeedList'

export function Activity() {
  const { logs } = useAgentFeed()

  return (
    <main className="p-6">
      <h1 className="text-2xl font-semibold tracking-tight text-[#e9e9ed] mb-4">Activity</h1>
      <AgentFeedList logs={logs} />
    </main>
  )
}
