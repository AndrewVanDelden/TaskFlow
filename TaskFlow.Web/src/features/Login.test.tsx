import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import App from '../App'

// After sign-in, the router navigates to the Board, which opens a SignalR connection.
// The shared manual mock at __mocks__/@microsoft/signalr.ts prevents a real negotiate.
vi.mock('@microsoft/signalr')

describe('Login flow', () => {
  it('signs in and stores the session', async () => {
    render(
      <MemoryRouter initialEntries={['/login']}>
        <App />
      </MemoryRouter>,
    )

    await userEvent.type(screen.getByPlaceholderText('Email'), 'ada@x.dev')
    await userEvent.type(screen.getByPlaceholderText('Password'), 'password1')
    await userEvent.click(screen.getByRole('button', { name: /sign in/i }))

    // App replaces Login with the Dashboard once sign-in resolves; findByRole waits for that
    // async re-render to land. Was previously asserted via visible "Ada" text in NavBar's header,
    // but the Sprint 1 icon-only SideBar has no visible username text anywhere (by design - every
    // icon-only control carries its accessible name via aria-label, not display text) - so the
    // session's username is now asserted directly against localStorage instead of inferred from
    // incidental UI text.
    expect(await screen.findByRole('heading', { name: 'Board' })).toBeInTheDocument()
    expect(localStorage.getItem('taskflow_token')).toBe('fake.jwt.token')
    expect(localStorage.getItem('taskflow_user')).toBe('Ada')
  })

  // Epic 3 Pre-Merge Code Review, finding 6.4: no test simulated a failed login, so the catch
  // block and its error banner had zero coverage.
  it('shows an alert and does not navigate away when sign-in fails', async () => {
    server.use(
      http.post('*/api/Auth/login', () => new HttpResponse('Invalid credentials.', { status: 401 })),
    )

    render(
      <MemoryRouter initialEntries={['/login']}>
        <App />
      </MemoryRouter>,
    )

    await userEvent.type(screen.getByPlaceholderText('Email'), 'ada@x.dev')
    await userEvent.type(screen.getByPlaceholderText('Password'), 'wrong-password')
    await userEvent.click(screen.getByRole('button', { name: /sign in/i }))

    const alert = await screen.findByRole('alert')
    expect(alert).not.toHaveTextContent('')
    expect(screen.getByRole('button', { name: /sign in/i })).toBeInTheDocument()
    expect(localStorage.getItem('taskflow_token')).toBeNull()
  })

  it('toggles between sign in and register', async () => {
    render(
      <MemoryRouter initialEntries={['/login']}>
        <App />
      </MemoryRouter>,
    )

    // Sign-in mode by default: no Name field, submit says "Sign in".
    expect(screen.queryByPlaceholderText('Name')).toBeNull()
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /need an account\? register/i }))

    // Register mode: Name field appears and the submit becomes "Create account".
    expect(screen.getByPlaceholderText('Name')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Create account' })).toBeInTheDocument()
  })
})
