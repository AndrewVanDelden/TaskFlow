import type { AgentLog } from '../types'
import { request } from './client'

export function getAgentLogs(limit = 50): Promise<AgentLog[]> {
  return request<AgentLog[]>(`/api/AgentLogs?limit=${limit}`)
}
