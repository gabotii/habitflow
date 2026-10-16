import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router'
import { Spinner } from '@/components/ui/display'
import { useAuth } from './auth-context'

/**
 * Hides private screens from anonymous visitors. This is a UX guard only: the API authorizes every request on the
 * server, so hiding routes is never what protects the data.
 */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { status } = useAuth()
  const location = useLocation()

  if (status === 'loading') {
    return (
      <div className="grid min-h-dvh place-items-center">
        <Spinner label="Comprobando la sesión…" />
      </div>
    )
  }

  if (status === 'anonymous') {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  }

  return children
}

/** Redirects signed-in users away from the login/register screens. */
export function RedirectIfAuthenticated({ children }: { children: ReactNode }) {
  const { status } = useAuth()
  const location = useLocation()
  // Back to the page that required the login (set by RequireAuth), or the dashboard.
  const from = (location.state as { from?: string } | null)?.from ?? '/'
  if (status === 'authenticated') return <Navigate to={from} replace />
  if (status === 'loading') {
    return (
      <div className="grid min-h-dvh place-items-center">
        <Spinner />
      </div>
    )
  }
  return children
}
