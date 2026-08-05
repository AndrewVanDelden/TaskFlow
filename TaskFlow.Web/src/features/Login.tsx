import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { login, register } from '../api/auth'
import { useAuth } from '../hooks/AuthContext'

const inputClass =
  'w-full px-3 py-2.5 rounded-lg bg-slate-950/60 text-white border border-slate-700 placeholder-slate-500 focus:outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500'

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
    <div className="min-h-screen flex items-center justify-center bg-slate-950 px-4">
      <div className="w-full max-w-sm">
        <div className="text-center mb-6">
          <span className="text-2xl font-bold text-white">TaskFlow</span>
          <p className="text-slate-500 text-sm mt-1">Autonomous agent workspace</p>
        </div>

        <form
          onSubmit={handleSubmit}
          className="bg-slate-900/70 border border-slate-800 rounded-2xl p-6 shadow-xl shadow-black/30"
        >
          <h1 className="text-lg font-semibold text-white mb-1">
            {isRegistering ? 'Create your account' : 'Welcome back'}
          </h1>
          <p className="text-slate-400 text-sm mb-5">
            {isRegistering ? 'Sign up to get started' : 'Sign in to continue'}
          </p>

          {isRegistering && (
            <input
              className={`${inputClass} mb-3`}
              placeholder="Name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
            />
          )}

          <input
            className={`${inputClass} mb-3`}
            type="email"
            placeholder="Email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
          />

          <input
            className={`${inputClass} mb-4`}
            type="password"
            placeholder="Password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />

          {error && (
            <div className="mb-4 text-sm text-red-300 bg-red-500/10 border border-red-500/30 rounded-lg px-3 py-2">
              {error}
            </div>
          )}

          <button
            type="submit"
            disabled={busy}
            className="w-full bg-blue-600 hover:bg-blue-500 disabled:opacity-50 text-white font-semibold py-2.5 rounded-lg transition-colors"
          >
            {busy ? 'Working...' : isRegistering ? 'Create account' : 'Sign in'}
          </button>

          <button
            type="button"
            onClick={() => {
              setIsRegistering(!isRegistering)
              setError(null)
            }}
            className="w-full mt-4 text-sm text-slate-400 hover:text-slate-200"
          >
            {isRegistering ? 'Already have an account? Sign in' : 'Need an account? Register'}
          </button>
        </form>
      </div>
    </div>
  )
}
