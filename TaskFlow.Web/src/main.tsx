import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import './index.css'
import App from './App.tsx'
import { clearAuthOnFreshDevServerBoot } from './lib/devAuthReset'

// Must run before App/AuthProvider ever reads a stored token (AuthProvider's initial state is
// getToken(), evaluated on first render) - see devAuthReset.ts for why.
clearAuthOnFreshDevServerBoot()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </StrictMode>,
)
