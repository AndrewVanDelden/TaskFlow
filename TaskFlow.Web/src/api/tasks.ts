import type { TaskItem, TaskStatus } from '../types'
import { request } from './client'

// `archived` always goes on the query string (not just when true) so the request the frontend
// sends is explicit either way, rather than relying on the server's own default matching ours.
export function getTasks(archived = false): Promise<TaskItem[]> {
  return request<TaskItem[]>(`/api/Tasks?archived=${archived}`)
}

export function updateTaskStatus(id: number, status: TaskStatus): Promise<TaskItem> {
  return request<TaskItem>(`/api/Tasks/${id}/status`, {
    method: 'PATCH',
    body: JSON.stringify({ status }),
  })
}

// Human sign-off: Review -> Done. Separate from updateTaskStatus so approval is an explicit action.
export function approveTask(id: number): Promise<TaskItem> {
  return request<TaskItem>(`/api/Tasks/${id}/approve`, { method: 'POST' })
}

// Human rejection: Review -> Todo with a required reason (rework).
export function rejectTask(id: number, reason: string): Promise<TaskItem> {
  return request<TaskItem>(`/api/Tasks/${id}/reject`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  })
}

// Board Done-column "archive" action: soft-archives a single Done task so it drops off the
// default board view but stays restorable via unarchiveTask.
export function archiveTask(id: number): Promise<TaskItem> {
  return request<TaskItem>(`/api/Tasks/${id}/archive`, { method: 'POST' })
}

// Restores a previously archived task back to the default board view.
export function unarchiveTask(id: number): Promise<TaskItem> {
  return request<TaskItem>(`/api/Tasks/${id}/unarchive`, { method: 'POST' })
}

// Board Done-column "clear all" bulk action: archives every Done task the caller can see.
export function archiveAllDone(): Promise<{ archivedCount: number }> {
  return request<{ archivedCount: number }>('/api/Tasks/archive-done', { method: 'POST' })
}
