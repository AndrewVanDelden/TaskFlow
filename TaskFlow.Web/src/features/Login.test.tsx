import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
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

    // MSW returns a token named "Ada"; App replaces Login with the Dashboard, whose header
    // shows the signed-in user. findByText waits for that async re-render to land.
    expect(await screen.findByText('Ada')).toBeInTheDocument()
    expect(localStorage.getItem('taskflow_token')).toBe('fake.jwt.token')
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
