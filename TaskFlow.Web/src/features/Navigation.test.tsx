import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import App from '../App'

// Authenticated screens open a SignalR connection; the shared manual mock prevents a real negotiate.
vi.mock('@microsoft/signalr')

// AuthProvider reads the token/name from localStorage; setting them makes the app "logged in".
function authenticate() {
  localStorage.setItem('taskflow_token', 'test.jwt.token')
  localStorage.setItem('taskflow_user', 'Ada')
}

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <App />
    </MemoryRouter>,
  )
}

describe('navigation', () => {
  it('shows the Ingest page when authenticated at /ingest', async () => {
    authenticate()
    renderAt('/ingest')
    expect(await screen.findByText('Ingest a document')).toBeInTheDocument()
  })

  it('redirects to the login form when unauthenticated', async () => {
    renderAt('/ingest')
    expect(await screen.findByPlaceholderText('Email')).toBeInTheDocument()
  })

  it('navigates from the board to the Ingest page via the nav link', async () => {
    authenticate()
    renderAt('/board')

    await userEvent.click(await screen.findByRole('link', { name: 'Ingest' }))

    expect(await screen.findByText('Ingest a document')).toBeInTheDocument()
  })
})
