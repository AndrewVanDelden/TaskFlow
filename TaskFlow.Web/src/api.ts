import type { AuthResponse, TaskItem, TaskStatus, AgentLog } from './types'

const BASE_URL = import.meta.env.VITE_API_BASE_URL as string

const TOKEN_KEY = 'taskflow_token'

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

export function setToken(token: string): void {
  localStorage.setItem(TOKEN_KEY, token)
}

export function clearToken(): void {
  localStorage.removeItem(TOKEN_KEY)
}

export class ApiError extends Error {
  status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

// Single place where every request gets its Authorization header.
async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const token = getToken()

  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(options.headers as Record<string, string> | undefined),
  }

  if (token) {
    headers['Authorization'] = `Bearer ${token}`
  }

  const response = await fetch(`${BASE_URL}${path}`, { ...options, headers })

  if (response.status === 401) {
    clearToken()
    throw new ApiError(401, 'Session expired. Please log in again.')
  }

  if (!response.ok) {
    const body = await response.text()
    throw new ApiError(response.status, body || response.statusText)
  }

  // 204 No Content has no body to parse
  if (response.status === 204) {
    return undefined as T
  }

  return response.json() as Promise<T>
}

// ── Auth ────────────────────────────────────────────────────────────────────
export function login(email: string, password: string): Promise<AuthResponse> {
  return request<AuthResponse>('/api/Auth/login', {
    method: 'POST',
    body: JSON.stringify({ email, password }),
  })
}

export function register(
  name: string,
  email: string,
  password: string,
): Promise<AuthResponse> {
  return request<AuthResponse>('/api/Auth/register', {
    method: 'POST',
    body: JSON.stringify({ name, email, password }),
  })
}

// ── Tasks ───────────────────────────────────────────────────────────────────
export function getTasks(): Promise<TaskItem[]> {
  return request<TaskItem[]>('/api/Tasks')
}

export function updateTaskStatus(id: number, status: TaskStatus): Promise<TaskItem> {
  return request<TaskItem>(`/api/Tasks/${id}/status`, {
    method: 'PATCH',
    body: JSON.stringify({ status }),
  })
}

// ── Agent logs ──────────────────────────────────────────────────────────────
export function getAgentLogs(limit = 50): Promise<AgentLog[]> {
  return request<AgentLog[]>(`/api/AgentLogs?limit=${limit}`)
}