// These mirror the DTOs in TaskFlow.Api/DTOs/.
// Keeping them in sync by hand is the trade-off for not generating a client.

export type TaskStatus = 'Todo' | 'InProgress' | 'Review' | 'Done'
export type TaskPriority = 'Low' | 'Medium' | 'High'
// Mirrors TaskFlow.Api/Models/TaskKind.cs.
export type TaskKind = 'Generic' | 'ResumeTailoring' | 'CoverLetterTailoring'

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
  kind: TaskKind
  applicationId: number | null
  tailoredContent: string | null
  // Sprint 5 review finding: a lone Epic-3 sibling task can reach 'Done' via the individual
  // per-task approve path while its own JobApplication never reaches Approved (only both siblings
  // approved together does that) - export-download gating needs this real state, not just Status.
  // Optional so existing fixtures that don't care about it don't need updating; absent/undefined
  // correctly fails an === 'Approved' check, matching the safe default (don't assume approved).
  applicationState?: string | null
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
  kind: TaskKind
  section: string
}

// Mirrors TaskFlow.Api's JobApplicationResponseDto: the approve/reject response shape.
export interface JobApplicationTaskSummary {
  id: number
  title: string
  kind: TaskKind
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

// Mirrors TaskFlow.Api's ResumeContextSummaryDto (GET .../resume-context/latest).
export interface ResumeContextSummary {
  content: string
  contentFormat: string
  updatedAt: string
}