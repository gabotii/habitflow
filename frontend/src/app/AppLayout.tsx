import {
  AlarmClock,
  BarChart3,
  CalendarDays,
  Home,
  ListChecks,
  LogOut,
  Handshake,
  Menu,
  Target,
  Trophy,
  UserRound,
  type LucideIcon,
} from 'lucide-react'
import { NavLink, Outlet, useNavigate } from 'react-router'
import { Logo } from '@/components/logo'
import { ThemeMenu } from '@/components/theme-menu'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { useAuth, useCurrentUser } from '@/features/auth/auth-context'
import { NotificationsMenu } from '@/features/reminders/NotificationsMenu'
import { cn } from '@/lib/utils'

interface NavItem {
  to: string
  label: string
  icon: LucideIcon
  end?: boolean
  mobile?: boolean
}

const NAV: NavItem[] = [
  { to: '/', label: 'Hoy', icon: Home, end: true, mobile: true },
  { to: '/habitos', label: 'Hábitos', icon: ListChecks, mobile: true },
  { to: '/calendario', label: 'Calendario', icon: CalendarDays, mobile: true },
  { to: '/objetivos', label: 'Objetivos', icon: Target, mobile: true },
  { to: '/estadisticas', label: 'Estadísticas', icon: BarChart3, mobile: true },
  { to: '/recordatorios', label: 'Recordatorios', icon: AlarmClock },
  { to: '/retos', label: 'Retos', icon: Trophy },
  { to: '/compartidos', label: 'Compartidos', icon: Handshake },
]

export function AppLayout() {
  const user = useCurrentUser()
  const { logout } = useAuth()
  const navigate = useNavigate()

  const signOut = async () => {
    await logout()
    navigate('/login', { replace: true })
  }

  return (
    <div className="min-h-dvh lg:grid lg:grid-cols-[15.5rem_1fr]">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-50 focus:rounded-lg focus:bg-surface focus:px-3 focus:py-2"
      >
        Saltar al contenido
      </a>

      <aside className="sticky top-0 hidden h-dvh flex-col bg-sidebar px-3 py-5 text-sidebar-foreground lg:flex">
        <div className="px-3">
          <Logo inverted />
        </div>
        <nav aria-label="Principal" className="mt-8 flex flex-1 flex-col gap-1">
          {NAV.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className={({ isActive }) =>
                cn(
                  'flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors hover:bg-sidebar-active hover:text-white',
                  isActive && 'bg-sidebar-active text-white',
                )
              }
            >
              <item.icon className="size-4" aria-hidden />
              {item.label}
            </NavLink>
          ))}
        </nav>
        <div className="border-t border-white/10 pt-4">
          <NavLink
            to="/perfil"
            className={({ isActive }) =>
              cn(
                'flex items-center gap-3 rounded-lg px-3 py-2 text-sm hover:bg-sidebar-active hover:text-white',
                isActive && 'bg-sidebar-active text-white',
              )
            }
          >
            <UserRound className="size-4" aria-hidden />
            <span className="min-w-0">
              <span className="block truncate font-medium text-white">{user.displayName}</span>
              <span className="block truncate text-xs">{user.email}</span>
            </span>
          </NavLink>
          <button
            type="button"
            onClick={signOut}
            className="mt-1 flex w-full items-center gap-3 rounded-lg px-3 py-2 text-sm hover:bg-sidebar-active hover:text-white"
          >
            <LogOut className="size-4" aria-hidden /> Cerrar sesión
          </button>
        </div>
      </aside>

      <div className="flex min-w-0 flex-col">
        <header className="sticky top-0 z-30 flex h-14 items-center justify-between gap-2 border-b border-border bg-surface/90 px-4 backdrop-blur lg:justify-end lg:px-8">
          <div className="lg:hidden">
            <Logo />
          </div>
          <div className="flex items-center gap-1">
            <ThemeMenu />
            <NotificationsMenu />
            {/* On mobile the bottom bar holds the five main sections; the rest lives in this menu. */}
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" size="icon" className="lg:hidden" aria-label="Más opciones">
                  <Menu />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                {NAV.filter((item) => !item.mobile).map((item) => (
                  <DropdownMenuItem key={item.to} onSelect={() => navigate(item.to)}>
                    <item.icon /> {item.label}
                  </DropdownMenuItem>
                ))}
                <DropdownMenuItem onSelect={() => navigate('/perfil')}>
                  <UserRound /> Perfil
                </DropdownMenuItem>
                <DropdownMenuSeparator />
                <DropdownMenuItem onSelect={signOut}>
                  <LogOut /> Cerrar sesión
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </header>
        <main id="main" className="mx-auto w-full max-w-7xl flex-1 px-4 pt-6 pb-24 lg:px-8 lg:pb-10">
          <Outlet />
        </main>
      </div>

      <nav
        aria-label="Navegación móvil"
        className="fixed inset-x-0 bottom-0 z-30 grid grid-cols-5 border-t border-border bg-surface pb-[env(safe-area-inset-bottom)] lg:hidden"
      >
        {NAV.filter((item) => item.mobile).map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.end}
            className={({ isActive }) =>
              cn(
                'flex flex-col items-center gap-0.5 py-2 text-[11px] font-medium text-muted-foreground',
                isActive && 'text-primary',
              )
            }
          >
            <item.icon className="size-5" aria-hidden />
            {item.label}
          </NavLink>
        ))}
      </nav>
    </div>
  )
}
