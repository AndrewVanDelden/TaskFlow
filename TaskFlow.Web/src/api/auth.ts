import type { AuthResponse } from '../types'
import { request } from './client'

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
