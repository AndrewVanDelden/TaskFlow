// Transport client: base URL, token storage, the shared request() that attaches the JWT,
// and the ApiError type. The endpoint modules (auth, tasks, agentLogs) build on request().

// Empty by default so the app is same-origin: the Vite dev proxy (and a prod host) serve /api and
// /hubs. Set VITE_API_BASE_URL only to point the frontend at a different API origin.
export const BASE_URL = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? ''

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
export async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
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
