import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import App from '../App'

// After sign-in, App swaps Login for the Dashboard, which opens a SignalR connection.
// The shared manual mock at __mocks__/@microsoft/signalr.ts prevents a real negotiate.
vi.mock('@microsoft/signalr')

describe('Login flow', () => {
  it('signs in and stores the session', async () => {
    render(<App />)

    await userEvent.type(screen.getByPlaceholderText('Email'), 'ada@x.dev')
    await userEvent.type(screen.getByPlaceholderText('Password'), 'password1')
    await userEvent.click(screen.getByRole('button', { name: /sign in/i }))

    // MSW returns a token named "Ada"; App replaces Login with the Dashboard, whose header
    // shows the signed-in user. findByText waits for that async re-render to land.
    expect(await screen.findByText('Ada')).toBeInTheDocument()
    expect(localStorage.getItem('taskflow_token')).toBe('fake.jwt.token')
  })
})
