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
  kind: string
  applicationId: number | null
  tailoredContent: string | null
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

// A proposed task returned by ingestion, before it is persisted to the board.
// Mirrors TaskFlow.Api/Ingestion/TaskDraft.cs (kind serializes as a string).
export interface TaskDraft {
  title: string
  description: string | null
  kind: string
  section: string
}

// Mirrors TaskFlow.Api's JobApplicationResponseDto: the approve/reject response shape.
export interface JobApplicationTaskSummary {
  id: number
  title: string
  kind: string
  status: string
}

export interface JobApplicationResponse {
  id: number
  state: string
  ingestionSessionId: string
  ownerId: number
  createdAt: string
  tasks: JobApplicationTaskSummary[]
}