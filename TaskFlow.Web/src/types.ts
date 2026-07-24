// These mirror the DTOs in TaskFlow.Api/DTOs/.
// Keeping them in sync by hand is the trade-off for not generating a client.

export type TaskStatus = 'Todo' | 'InProgress' | 'Review' | 'Done'
export type TaskPriority = 'Low' | 'Medium' | 'High'

export interface TaskItem {
  id: number
  title: string
  description: string | null
  status: TaskStatus
  priority: TaskPriority
  dueDate: string | null
  createdAt: string
  updatedAt: string
  assignedToId: number | null
  assignedToName: string | null
}

export interface AuthResponse {
  token: string
  name: string
  email: string
  expiresAt: string
}

export interface AgentLog {
  id: number
  taskId: number | null
  agentName: string
  action: string
  details: string | null
  success: boolean
  createdAt: string
}