import { useQueryClient } from '@tanstack/react-query'
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { apiRequest, ensureCsrfToken, setUnauthorizedHandler } from '@/lib/api-client'
import type { User } from '@/types/api'

type AuthStatus = 'loading' | 'authenticated' | 'anonymous'

export interface RegisterInput {
  displayName: string
  email: string
  password: string
  timeZoneId: string
}

interface AuthContextValue {
  status: AuthStatus
  user: User | null
  login: (email: string, password: string, rememberMe: boolean) => Promise<void>
  register: (input: RegisterInput) => Promise<void>
  logout: () => Promise<void>
  setUser: (user: User) => void
  /** Forget the session locally (after account deletion or a 401). */
  clearSession: () => void
}

const AuthContext = createContext<AuthContextValue | null>(null)

/**
 * The session lives in an HttpOnly cookie the page cannot read, so on load we ask the API who we are. The server
 * authorizes every request; this context only drives navigation and display.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [status, setStatus] = useState<AuthStatus>('loading')
  const [user, setUserState] = useState<User | null>(null)

  const signedIn = useCallback((value: User) => {
    setUserState(value)
    setStatus('authenticated')
  }, [])

  const clearSession = useCallback(() => {
    setUserState(null)
    setStatus('anonymous')
    queryClient.clear()
  }, [queryClient])

  useEffect(() => {
    let cancelled = false
    apiRequest<User>('/users/me', { anonymous: true })
      .then((me) => {
        if (!cancelled) signedIn(me)
      })
      .catch(() => {
        // 401, or the API is unreachable: either way the login screen is where the user can act.
        if (!cancelled) setStatus('anonymous')
      })
    return () => {
      cancelled = true
    }
  }, [signedIn])

  useEffect(() => {
    setUnauthorizedHandler(clearSession)
    return () => setUnauthorizedHandler(null)
  }, [clearSession])

  const value = useMemo<AuthContextValue>(
    () => ({
      status,
      user,
      setUser: setUserState,
      clearSession,
      login: async (email, password, rememberMe) => {
        await ensureCsrfToken()
        signedIn(
          await apiRequest<User>('/auth/login', {
            method: 'POST',
            body: { email, password, rememberMe },
            anonymous: true,
          }),
        )
      },
      register: async (input) => {
        await ensureCsrfToken()
        signedIn(await apiRequest<User>('/auth/register', { method: 'POST', body: input, anonymous: true }))
      },
      logout: async () => {
        try {
          await apiRequest<void>('/auth/logout', { method: 'POST', anonymous: true })
        } finally {
          clearSession()
        }
      },
    }),
    [status, user, signedIn, clearSession],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside <AuthProvider>')
  return context
}

/** The signed-in user; only call inside routes protected by <RequireAuth>. */
export function useCurrentUser(): User {
  const { user } = useAuth()
  if (!user) throw new Error('useCurrentUser requires an authenticated session')
  return user
}
