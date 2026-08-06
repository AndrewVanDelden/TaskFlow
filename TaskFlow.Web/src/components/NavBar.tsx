import { NavLink } from 'react-router-dom'
import { useAuth } from '../hooks/AuthContext'

const linkClass = ({ isActive }: { isActive: boolean }) =>
  `text-sm px-3 py-1.5 rounded ${
    isActive ? 'bg-slate-800 text-white' : 'text-slate-400 hover:text-white'
  }`

// Shared header for every authenticated screen: brand, primary nav, and the signed-in user + sign out.
export function NavBar() {
  const { userName, signOut } = useAuth()

  return (
    <header className="border-b border-slate-800 px-6 py-3 flex items-center justify-between">
      <div className="flex items-center gap-6">
        <div>
          <h1 className="text-lg font-bold">TaskFlow</h1>
          <p className="text-xs text-slate-500">Autonomous agent workspace</p>
        </div>
        <nav className="flex items-center gap-1">
          <NavLink to="/board" className={linkClass}>
            Board
          </NavLink>
          <NavLink to="/ingest" className={linkClass}>
            Ingest
          </NavLink>
        </nav>
      </div>

      <div className="flex items-center gap-3">
        <span className="text-sm text-slate-400">{userName}</span>
        <button
          onClick={signOut}
          className="text-xs border border-slate-700 hover:border-slate-600 px-3 py-1.5 rounded"
        >
          Sign out
        </button>
      </div>
    </header>
  )
}
