import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { login, register } from '../api/auth'
import { useAuth } from '../hooks/AuthContext'
import { Button } from '../components/ui/Button'
import { bgPage, bgSurface, textNeutral500, textPrimary, focusRingAccent } from '../lib/tokens'

const inputClass = `w-full h-10 px-3 rounded-lg bg-[#232532] border border-white/10 text-white placeholder-slate-500 ${focusRingAccent}`

const labelClass = `block mb-1 text-xs ${textNeutral500}`

// Static placeholder copy for the brand pane - not wired to any live data source (locked spec).
const teasers = [
  'Executor tailoring Anthropic resume…',
  'Notion application ready for review',
  '2 applications submitted today',
]

export function Login() {
  const { signIn } = useAuth()
  const navigate = useNavigate()
  const [isRegistering, setIsRegistering] = useState(false)
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    setBusy(true)

    try {
      const result = isRegistering
        ? await register(name, email, password)
        : await login(email, password)

      signIn(result.token, result.name)
      navigate('/board')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Something went wrong.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className={`min-h-screen flex items-center justify-center ${bgPage} px-4`}>
      <div className="w-full max-w-[920px] min-h-[560px] flex rounded-2xl overflow-hidden border border-white/10 shadow-xl shadow-black/30">
        <div className="hidden md:flex w-1/2 flex-col justify-center gap-10 p-10 bg-gradient-to-br from-[#423a6a] to-[#161826]">
          <div>
            <span className={`text-2xl font-bold ${textPrimary}`}>TaskFlow</span>
            <p className="text-slate-300 text-sm mt-1">Your autonomous application workspace</p>
          </div>

          <ul className="space-y-3">
            {teasers.map((teaser) => (
              <li key={teaser} className="flex items-center gap-2 text-sm text-slate-300">
                <span className="h-1.5 w-1.5 rounded-full bg-[#9184d9]" />
                {teaser}
              </li>
            ))}
          </ul>
        </div>

        <div className={`w-full md:w-1/2 ${bgSurface} p-8 flex flex-col justify-center`}>
          <form onSubmit={handleSubmit}>
            <h1 className={`text-lg font-semibold ${textPrimary} mb-1`}>
              {isRegistering ? 'Create your account' : 'Welcome back'}
            </h1>
            <p className="text-slate-400 text-sm mb-5">
              {isRegistering ? 'Sign up to get started' : 'Sign in to continue'}
            </p>

            {isRegistering && (
              <div className="mb-3">
                <label htmlFor="login-name" className={labelClass}>
                  Name
                </label>
                <input
                  id="login-name"
                  className={inputClass}
                  placeholder="Name"
                  aria-label="Name"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  required
                />
              </div>
            )}

            <div className="mb-3">
              <label htmlFor="login-email" className={labelClass}>
                Email
              </label>
              <input
                id="login-email"
                className={inputClass}
                type="email"
                placeholder="Email"
                aria-label="Email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
              />
            </div>

            <div className="mb-4">
              <label htmlFor="login-password" className={labelClass}>
                Password
              </label>
              <input
                id="login-password"
                className={inputClass}
                type="password"
                placeholder="Password"
                aria-label="Password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
              />
            </div>

            {error && (
              <div role="alert" className="mb-4 text-sm text-red-300 bg-red-500/10 border border-red-500/30 rounded-lg px-3 py-2">
                {error}
              </div>
            )}

            <Button type="submit" variant="primary" className="w-full" disabled={busy}>
              {busy ? 'Working...' : isRegistering ? 'Create account' : 'Sign in'}
            </Button>

            <Button
              type="button"
              variant="ghost"
              className="w-full mt-4 text-sm"
              onClick={() => {
                setIsRegistering(!isRegistering)
                setError(null)
              }}
            >
              {isRegistering ? 'Already have an account? Sign in' : 'Create an account'}
            </Button>
          </form>
        </div>
      </div>
    </div>
  )
}
