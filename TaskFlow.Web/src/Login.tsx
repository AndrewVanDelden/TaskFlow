import { useState, type FormEvent } from 'react'
import { login, register } from './api'
import { useAuth } from './AuthContext'

export function Login() {
  const { signIn } = useAuth()
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
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Something went wrong.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-900">
      <form
        onSubmit={handleSubmit}
        className="bg-slate-800 p-8 rounded-xl w-full max-w-sm border border-slate-700"
      >
        <h1 className="text-2xl font-bold text-white mb-1">TaskFlow</h1>
        <p className="text-slate-400 text-sm mb-6">
          {isRegistering ? 'Create an account' : 'Sign in to continue'}
        </p>

        {isRegistering && (
          <input
            className="w-full mb-3 px-3 py-2 rounded bg-slate-900 text-white border border-slate-700 focus:outline-none focus:border-blue-500"
            placeholder="Name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
          />
        )}

        <input
          className="w-full mb-3 px-3 py-2 rounded bg-slate-900 text-white border border-slate-700 focus:outline-none focus:border-blue-500"
          type="email"
          placeholder="Email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
        />

        <input
          className="w-full mb-4 px-3 py-2 rounded bg-slate-900 text-white border border-slate-700 focus:outline-none focus:border-blue-500"
          type="password"
          placeholder="Password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />

        {error && (
          <div className="mb-4 text-sm text-red-400 bg-red-950 border border-red-900 rounded px-3 py-2">
            {error}
          </div>
        )}

        <button
          type="submit"
          disabled={busy}
          className="w-full bg-blue-600 hover:bg-blue-500 disabled:opacity-50 text-white font-semibold py-2 rounded"
        >
          {busy ? 'Working...' : isRegistering ? 'Create account' : 'Sign in'}
        </button>

        <button
          type="button"
          onClick={() => {
            setIsRegistering(!isRegistering)
            setError(null)
          }}
          className="w-full mt-3 text-sm text-slate-400 hover:text-slate-200"
        >
          {isRegistering
            ? 'Already have an account? Sign in'
            : 'Need an account? Register'}
        </button>
      </form>
    </div>
  )
}