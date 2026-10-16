import { SearchX } from 'lucide-react'
import { createBrowserRouter, Link, type RouteObject } from 'react-router'
import { EmptyState } from '@/components/page'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/display'
import { ForgotPasswordPage, LoginPage, RegisterPage, ResetPasswordPage } from '@/features/auth/AuthPages'
import { RedirectIfAuthenticated, RequireAuth } from '@/features/auth/RequireAuth'
import { AppLayout } from './AppLayout'
import { RouteError } from './RouteError'

function NotFound() {
  return (
    <EmptyState
      icon={SearchX}
      title="Página no encontrada"
      description="La dirección no existe o el recurso ya no está disponible."
      action={
        <Button asChild>
          <Link to="/">Volver al inicio</Link>
        </Button>
      }
    />
  )
}

const anonymous = (element: React.ReactNode) => <RedirectIfAuthenticated>{element}</RedirectIfAuthenticated>

// Feature pages are code-split so heavy libraries (charts, calendar) load only when needed.
export const routes: RouteObject[] = [
  { path: '/login', element: anonymous(<LoginPage />) },
  { path: '/registro', element: anonymous(<RegisterPage />) },
  { path: '/recuperar-contrasena', element: anonymous(<ForgotPasswordPage />) },
  { path: '/restablecer-contrasena', element: <ResetPasswordPage /> },
  {
    path: '/',
    element: (
      <RequireAuth>
        <AppLayout />
      </RequireAuth>
    ),
    errorElement: <RouteError />,
    // Shown while the first lazy page chunk loads.
    HydrateFallback: () => (
      <div className="grid min-h-dvh place-items-center">
        <Spinner />
      </div>
    ),
    children: [
      {
        index: true,
        lazy: () =>
          import('@/features/dashboard/DashboardPage').then((m) => ({ Component: m.DashboardPage })),
      },
      {
        path: 'habitos',
        lazy: () => import('@/features/habits/HabitsPage').then((m) => ({ Component: m.HabitsPage })),
      },
      {
        path: 'habitos/:id',
        lazy: () =>
          import('@/features/habits/HabitDetailPage').then((m) => ({ Component: m.HabitDetailPage })),
      },
      {
        path: 'calendario',
        lazy: () => import('@/features/calendar/CalendarPage').then((m) => ({ Component: m.CalendarPage })),
      },
      {
        path: 'objetivos',
        lazy: () => import('@/features/goals/GoalsPage').then((m) => ({ Component: m.GoalsPage })),
      },
      {
        path: 'objetivos/:id',
        lazy: () => import('@/features/goals/GoalDetailPage').then((m) => ({ Component: m.GoalDetailPage })),
      },
      {
        path: 'estadisticas',
        lazy: () =>
          import('@/features/statistics/StatisticsPage').then((m) => ({ Component: m.StatisticsPage })),
      },
      {
        path: 'recordatorios',
        lazy: () =>
          import('@/features/reminders/RemindersPage').then((m) => ({ Component: m.RemindersPage })),
      },
      {
        path: 'retos',
        lazy: () =>
          import('@/features/challenges/ChallengesPages').then((m) => ({ Component: m.ChallengesPage })),
      },
      {
        path: 'retos/:id',
        lazy: () =>
          import('@/features/challenges/ChallengesPages').then((m) => ({ Component: m.ChallengeDetailPage })),
      },
      {
        path: 'compartidos',
        lazy: () =>
          import('@/features/shared-goals/SharedGoalsPages').then((m) => ({ Component: m.SharedGoalsPage })),
      },
      {
        path: 'compartidos/:id',
        lazy: () =>
          import('@/features/shared-goals/SharedGoalsPages').then((m) => ({
            Component: m.SharedGoalDetailPage,
          })),
      },
      {
        path: 'perfil',
        lazy: () => import('@/features/profile/ProfilePage').then((m) => ({ Component: m.ProfilePage })),
      },
      { path: '*', element: <NotFound /> },
    ],
  },
]

export const router = createBrowserRouter(routes)
