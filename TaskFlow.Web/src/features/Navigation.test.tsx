import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import { MemoryRouter, useLocation } from 'react-router-dom'
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

// A sibling probe rendered inside the same MemoryRouter as <App/>, so tests can observe the
// pathname the router actually settles on after any redirect - without reaching into App's
// internals. Reading useLocation() during render (not an effect) is fine here: it only assigns a
// closured variable for the test to read later, it never triggers a re-render itself.
function LocationProbe({ onLocation }: { onLocation: (pathname: string) => void }) {
  onLocation(useLocation().pathname)
  return null
}

function renderAtWithLocationProbe(path: string) {
  let pathname = ''
  const utils = render(
    <MemoryRouter initialEntries={[path]}>
      <LocationProbe onLocation={(p) => (pathname = p)} />
      <App />
    </MemoryRouter>,
  )
  return { ...utils, getPathname: () => pathname }
}

// A UUID (crypto.randomUUID() shape) as the second /ingest/... path segment.
const SESSION_URL = /^\/ingest\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

describe('navigation', () => {
  it('shows the Ingest page when authenticated at /ingest', async () => {
    authenticate()
    renderAt('/ingest')
    expect(await screen.findByText('Start a job application')).toBeInTheDocument()
  })

  it('redirects to the login form when unauthenticated', async () => {
    renderAt('/ingest')
    expect(await screen.findByPlaceholderText('Email')).toBeInTheDocument()
  })

  it('navigates from the board to the Ingest page via the nav link', async () => {
    authenticate()
    renderAt('/board')

    await userEvent.click(await screen.findByRole('link', { name: 'Ingest' }))

    expect(await screen.findByText('Start a job application')).toBeInTheDocument()
  })

  it('redirects bare /ingest to a session-id-bearing URL', async () => {
    authenticate()
    const { getPathname } = renderAtWithLocationProbe('/ingest')

    await screen.findByText('Start a job application')

    expect(getPathname()).toMatch(SESSION_URL)
  })

  it('two separate visits to bare /ingest get different session ids', async () => {
    authenticate()
    const first = renderAtWithLocationProbe('/ingest')
    await screen.findByText('Start a job application')
    const firstPathname = first.getPathname()
    first.unmount()

    const second = renderAtWithLocationProbe('/ingest')
    await screen.findByText('Start a job application')
    const secondPathname = second.getPathname()

    expect(firstPathname).toMatch(SESSION_URL)
    expect(secondPathname).toMatch(SESSION_URL)
    expect(firstPathname).not.toBe(secondPathname)
  })

  it('shows the Activity page when authenticated at /activity', async () => {
    authenticate()
    renderAt('/activity')

    expect(await screen.findByRole('heading', { name: 'Activity' })).toBeInTheDocument()
  })

  it('signing out from the sidebar redirects to the login form', async () => {
    authenticate()
    renderAt('/board')

    await userEvent.click(await screen.findByRole('button', { name: 'Account' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Sign out' }))

    expect(await screen.findByPlaceholderText('Email')).toBeInTheDocument()
  })

  it('gives the shell a bounded height so the sidebar stays pinned while content scrolls', async () => {
    authenticate()
    renderAt('/board')

    const nav = await screen.findByRole('navigation')
    const shell = nav.parentElement as HTMLElement

    expect(getComputedStyle(shell).height).toBe('100vh')
  })
})
