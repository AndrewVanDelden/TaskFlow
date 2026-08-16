import { useEffect, useState } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import { KanbanIcon, TrayIcon, PulseIcon, ArchiveIcon, UserCircleIcon } from '@phosphor-icons/react'
import { useAuth } from '../hooks/AuthContext'
import { Button } from './ui/Button'
import { textNeutral500 } from '../lib/tokens'

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  `flex items-center justify-center w-10 h-10 rounded-[10px] ${
    isActive ? 'bg-[#9184d9]/15 text-[#e7e5fe]' : textNeutral500
  }`

// Fixed-width icon rail: primary nav (top) plus the account menu (pinned to the bottom via mt-auto).
export function SideBar() {
  const { signOut } = useAuth()
  const [menuOpen, setMenuOpen] = useState(false)
  const location = useLocation()

  useEffect(() => {
    setMenuOpen(false)
  }, [location.pathname])

  return (
    <nav className="w-[60px] h-screen flex flex-col items-center py-4 gap-2">
      <NavLink to="/board" aria-label="Board" className={navLinkClass}>
        <KanbanIcon aria-hidden="true" />
      </NavLink>
      <NavLink to="/ingest" aria-label="Ingest" className={navLinkClass}>
        <TrayIcon aria-hidden="true" />
      </NavLink>
      <NavLink to="/activity" aria-label="Activity" className={navLinkClass}>
        <PulseIcon aria-hidden="true" />
      </NavLink>
      <NavLink to="/archive" aria-label="Archive" className={navLinkClass}>
        <ArchiveIcon aria-hidden="true" />
      </NavLink>

      <div className="mt-auto relative">
        <button
          type="button"
          aria-label="Account"
          aria-haspopup="true"
          aria-expanded={menuOpen}
          onClick={() => setMenuOpen((open) => !open)}
          className={`flex items-center justify-center w-10 h-10 rounded-[10px] ${textNeutral500}`}
        >
          <UserCircleIcon aria-hidden="true" />
        </button>
        {menuOpen && (
          <div className="absolute bottom-0 left-full ml-2 whitespace-nowrap">
            <Button variant="ghost" onClick={signOut}>
              Sign out
            </Button>
          </div>
        )}
      </div>
    </nav>
  )
}
