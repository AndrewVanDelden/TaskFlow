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

// Shared by request() and requestBlob(): every call gets its Authorization header attached the
// same way, whatever else the caller passes in.
function buildHeaders(extraHeaders?: Record<string, string>): Record<string, string> {
  const token = getToken()
  const headers: Record<string, string> = { ...extraHeaders }

  if (token) {
    headers['Authorization'] = `Bearer ${token}`
  }

  return headers
}

// The server's error responses (ToActionResult / ToFileActionResult) are JSON objects shaped
// { "message": "..." } - extracts that field so the UI shows the real message instead of the raw
// JSON text. Returns null (rather than throwing) for a non-JSON or differently-shaped body, so the
// caller can fall back to the raw text.
function extractErrorMessage(body: string): string | null {
  try {
    const parsed = JSON.parse(body)
    return typeof parsed?.message === 'string' ? parsed.message : null
  } catch {
    return null
  }
}

// Shared by request() and requestBlob(): the same 401-clears-token behavior, and the same
// status+message ApiError for every other non-OK response.
async function throwForErrorResponse(response: Response): Promise<never> {
  if (response.status === 401) {
    clearToken()
    throw new ApiError(401, 'Session expired. Please log in again.')
  }

  const body = await response.text()
  throw new ApiError(response.status, extractErrorMessage(body) ?? (body || response.statusText))
}

// Single place where every JSON request gets its Authorization header.
export async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const headers = buildHeaders({
    'Content-Type': 'application/json',
    ...(options.headers as Record<string, string> | undefined),
  })

  const response = await fetch(`${BASE_URL}${path}`, { ...options, headers })

  if (!response.ok) {
    await throwForErrorResponse(response)
  }

  // 204 No Content has no body to parse
  if (response.status === 204) {
    return undefined as T
  }

  return response.json() as Promise<T>
}

export interface DownloadedFile {
  blob: Blob
  filename: string
}

// Parses the standard `Content-Disposition: attachment; filename="resume.pdf"` shape. Returns null
// (rather than throwing) when the header is missing or doesn't match, so callers can fall back.
function extractFilename(contentDisposition: string | null): string | null {
  if (!contentDisposition) {
    return null
  }
  const match = /filename="?([^";]+)"?/i.exec(contentDisposition)
  return match ? match[1] : null
}

// Sibling to request() for binary/text file downloads: same auth header and error handling, but
// reads the body as a Blob instead of parsing it as JSON, and surfaces the filename the server
// named via Content-Disposition.
export async function requestBlob(path: string, options: RequestInit = {}): Promise<DownloadedFile> {
  const headers = buildHeaders(options.headers as Record<string, string> | undefined)

  const response = await fetch(`${BASE_URL}${path}`, { ...options, headers })

  if (!response.ok) {
    await throwForErrorResponse(response)
  }

  const blob = await response.blob()
  const filename = extractFilename(response.headers.get('Content-Disposition')) ?? 'download'
  return { blob, filename }
}

// Sibling to request() for multipart file uploads: same auth header and error handling, but takes a
// FormData body and deliberately does NOT set Content-Type - the browser sets it itself (including
// the multipart boundary), which a manual 'multipart/form-data' header would break.
export async function requestFormData<T>(path: string, formData: FormData): Promise<T> {
  const headers = buildHeaders()

  const response = await fetch(`${BASE_URL}${path}`, { method: 'POST', body: formData, headers })

  if (!response.ok) {
    await throwForErrorResponse(response)
  }

  return response.json() as Promise<T>
}
