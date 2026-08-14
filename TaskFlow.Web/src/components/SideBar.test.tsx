import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import { SideBar } from './SideBar'
import { axe } from '../test/axe'

const { signOut } = vi.hoisted(() => ({ signOut: vi.fn() }))

vi.mock('../hooks/AuthContext', () => ({
  useAuth: () => ({ isAuthenticated: true, userName: 'Ada', signIn: vi.fn(), signOut }),
}))

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <SideBar />
    </MemoryRouter>,
  )
}

describe('SideBar', () => {
  it('renders Board, Ingest, and Activity nav links', () => {
    renderAt('/board')

    expect(screen.getByRole('link', { name: 'Board' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Ingest' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Activity' })).toBeInTheDocument()
  })

  it('marks the active nav item with the active classes, leaving inactive items unstyled', () => {
    renderAt('/board')

    const board = screen.getByRole('link', { name: 'Board' })
    const ingest = screen.getByRole('link', { name: 'Ingest' })

    expect(board.className).toContain('bg-[#9184d9]/15')
    expect(ingest.className).not.toContain('bg-[#9184d9]/15')
  })

  it('reveals a Sign out control when the avatar is clicked, and calls signOut when clicked', async () => {
    renderAt('/board')
    const user = userEvent.setup()

    expect(screen.queryByRole('button', { name: 'Sign out' })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Account' }))
    const signOutButton = screen.getByRole('button', { name: 'Sign out' })
    await user.click(signOutButton)

    expect(signOut).toHaveBeenCalledTimes(1)
  })

  it('has no accessibility violations', async () => {
    const { container } = renderAt('/board')

    expect(await axe(container)).toHaveNoViolations()
  })
})
