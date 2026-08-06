import { request } from './client'

export interface ExecutorState {
  enabled: boolean
}

export const getExecutorState = (): Promise<ExecutorState> =>
  request<ExecutorState>('/api/agents/executor')

export const enableExecutor = (): Promise<ExecutorState> =>
  request<ExecutorState>('/api/agents/executor/enable', { method: 'POST' })

export const disableExecutor = (): Promise<ExecutorState> =>
  request<ExecutorState>('/api/agents/executor/disable', { method: 'POST' })
