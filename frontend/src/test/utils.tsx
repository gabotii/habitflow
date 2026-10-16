import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import type { ReactNode } from 'react'
import { createMemoryRouter, RouterProvider, type RouteObject } from 'react-router'
import { Toaster } from 'sonner'
import { AuthProvider } from '@/features/auth/auth-context'
import { RequireAuth } from '@/features/auth/RequireAuth'
import type { User } from '@/types/api'
import { server } from './server'

export const API = 'http://localhost:3000/api/v1'

export const testUser: User = {
  id: '00000000-0000-0000-0000-000000000001',
  email: 'ana@test.local',
  displayName: 'Ana',
  timeZoneId: 'Europe/Madrid',
  emailRemindersEnabled: false,
  createdAt: '2026-01-01T00:00:00.000Z',
}

/** The startup "who am I" call: signed in or anonymous. Also serves the anti-forgery endpoint. */
export function mockSession(signedIn: boolean) {
  server.use(
    http.get(`${API}/users/me`, () =>
      signedIn ? HttpResponse.json(testUser) : problem(401, 'Debes iniciar sesión.', 'unauthorized'),
    ),
    http.get(`${API}/auth/csrf`, () => {
      document.cookie = 'XSRF-TOKEN=test-csrf; path=/'
      return new HttpResponse(null, { status: 204 })
    }),
    http.get(`${API}/auth/capabilities`, () =>
      HttpResponse.json({ passwordResetAvailable: true, emailRemindersAvailable: false }),
    ),
  )
}

export function problem(status: number, detail: string, code = 'error', errors?: Record<string, string[]>) {
  return HttpResponse.json({ status, title: 'Error', detail, code, errors }, { status })
}

interface Options {
  route?: string
  path?: string
  signedIn?: boolean
  extraRoutes?: RouteObject[]
}

/** Renders a screen inside the real providers (TanStack Query, auth, router, toasts). */
export function renderWithProviders(
  ui: ReactNode,
  { route = '/', path = '/', signedIn = true, extraRoutes = [] }: Options = {},
) {
  mockSession(signedIn)
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const router = createMemoryRouter(
    [
      // Private screens are rendered behind the same guard the app uses, so the session exists when they mount.
      { path, element: signedIn ? <RequireAuth>{ui}</RequireAuth> : ui },
      { path: '/login', element: <p>Pantalla de login</p> },
      ...extraRoutes,
    ],
    { initialEntries: [route] },
  )
  const result = render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
      <Toaster />
    </QueryClientProvider>,
  )
  return { ...result, router, queryClient }
}
