import type { TaskItem, TaskStatus } from '../types'
import { request } from './client'

export function getTasks(): Promise<TaskItem[]> {
  return request<TaskItem[]>('/api/Tasks')
}

export function updateTaskStatus(id: number, status: TaskStatus): Promise<TaskItem> {
  return request<TaskItem>(`/api/Tasks/${id}/status`, {
    method: 'PATCH',
    body: JSON.stringify({ status }),
  })
}
