import { useQuery } from '@tanstack/react-query'
import {
  ArrowRight,
  BellRing,
  CalendarRange,
  CircleCheck,
  Flame,
  ListTodo,
  Plus,
  Sparkles,
  Target,
  Trophy,
} from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router'
import { HabitIcon, ProgressBar, ProgressRing, StreakBadge } from '@/components/habit-visuals'
import { ErrorState } from '@/components/page'
import { StatTile } from '@/components/stat-tile'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle, Skeleton } from '@/components/ui/display'
import { LogControl } from '@/features/habit-logs/LogControl'
import { HabitFormDialog } from '@/features/habits/HabitFormDialog'
import { ChartCard, RateLineChart } from '@/features/statistics/charts'
import { api } from '@/lib/api-client'
import {
  capitalize,
  formatDayMonth,
  formatInstant,
  formatLongDate,
  formatRelative,
  formatShortDate,
} from '@/lib/dates'
import { describeSchedule, unitLabel } from '@/lib/labels'
import { formatNumber, formatPercent } from '@/lib/utils'
import type { Activity, Dashboard, HabitListItem } from '@/types/api'

export function useDashboard() {
  return useQuery({ queryKey: ['dashboard'], queryFn: () => api.get<Dashboard>('/dashboard') })
}

function greeting(): string {
  const hour = new Date().getHours()
  if (hour < 6) return 'Buenas noches'
  if (hour < 13) return 'Buenos días'
  if (hour < 21) return 'Buenas tardes'
  return 'Buenas noches'
}

export function DashboardPage() {
  const dashboard = useDashboard()
  const [creating, setCreating] = useState(false)

  if (dashboard.isPending) return <DashboardSkeleton />
  if (dashboard.isError) return <ErrorState error={dashboard.error} onRetry={() => dashboard.refetch()} />
  const d = dashboard.data

  return (
    <>
      <div className="mb-6 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">
            {greeting()}, {d.displayName}
          </h1>
          <p className="mt-1 text-sm text-muted-foreground">
            {capitalize(formatLongDate(d.today))} · {d.timeZoneId}
          </p>
        </div>
        <Button onClick={() => setCreating(true)}>
          <Plus /> Nuevo hábito
        </Button>
      </div>

      {!d.hasHabits ? (
        <Onboarding onCreate={() => setCreating(true)} />
      ) : (
        <div className="grid grid-cols-1 gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
          <div className="min-w-0 space-y-6">
            <TodayHero dashboard={d} />
            <TodayLists dashboard={d} />
            <ChartCard
              title="Cumplimiento de los últimos 30 días"
              subtitle="% de hábitos diarios programados que completaste cada día"
              rows={d.last30Days}
              columns={[
                { header: 'Fecha', cell: (r) => formatShortDate(r.date) },
                { header: 'Completados', cell: (r) => `${r.completed} de ${r.scheduled}`, numeric: true },
                { header: 'Cumplimiento', cell: (r) => formatPercent(r.rate), numeric: true },
              ]}
              empty={
                d.last30Days.every((p) => p.scheduled === 0)
                  ? 'Todavía no hay días programados que medir.'
                  : undefined
              }
            >
              <RateLineChart
                data={d.last30Days}
                xKey="date"
                yKey="rate"
                name="cumplido"
                formatX={formatDayMonth}
              />
            </ChartCard>
          </div>
          <aside className="space-y-6">
            <StreakCards dashboard={d} />
            <WeekCard dashboard={d} />
            <GoalsCard dashboard={d} />
            <RemindersCard dashboard={d} />
            <ActivityCard activity={d.recentActivity} />
          </aside>
        </div>
      )}
      <HabitFormDialog open={creating} onOpenChange={setCreating} />
    </>
  )
}

function TodayHero({ dashboard: d }: { dashboard: Dashboard }) {
  const { scheduled, completed, percent } = d.todaySummary
  const message =
    scheduled === 0
      ? 'Hoy no tienes hábitos diarios programados. Aprovecha para avanzar en tus metas semanales.'
      : completed === scheduled
        ? '¡Día completo! Cada día así suma a tu racha.'
        : completed === 0
          ? 'Un primer paso pequeño basta para arrancar el día.'
          : `Te quedan ${scheduled - completed} para cerrar el día.`
  return (
    <Card className="flex flex-col items-center gap-5 p-5 sm:flex-row">
      <div className="relative">
        <ProgressRing percent={percent} />
        <div className="absolute inset-0 grid place-items-center text-center">
          <div>
            <p className="text-2xl font-extrabold">{percent === null ? '—' : `${Math.round(percent)}%`}</p>
            <p className="text-xs text-muted-foreground">hoy</p>
          </div>
        </div>
      </div>
      <div className="flex-1 text-center sm:text-left">
        <p className="text-lg font-semibold">
          {scheduled === 0
            ? 'Sin hábitos diarios hoy'
            : `${completed} de ${scheduled} hábitos de hoy completados`}
        </p>
        <p className="mt-1 text-sm text-muted-foreground">{message}</p>
        {d.weekly.length > 0 ? (
          <p className="mt-2 text-xs text-muted-foreground">
            Los hábitos con meta semanal se miden por semana y no cuentan en el porcentaje diario.
          </p>
        ) : null}
      </div>
    </Card>
  )
}

function HabitRow({ habit }: { habit: HabitListItem }) {
  return (
    <li className="flex items-center gap-3 py-3">
      <HabitIcon icon={habit.icon} color={habit.color} size="sm" />
      <div className="min-w-0 flex-1">
        <Link to={`/habitos/${habit.id}`} className="block truncate text-sm font-semibold hover:underline">
          {habit.name}
        </Link>
        <div className="mt-0.5 flex flex-wrap items-center gap-2">
          <StreakBadge length={habit.streak.current} unit={habit.streak.unit} />
          {habit.week ? (
            <span className="text-xs text-muted-foreground">
              Semana {habit.week.completed}/{habit.week.required}
              {habit.week.achieved ? ' · cumplida' : ` · quedan ${habit.week.daysLeft} días`}
            </span>
          ) : (
            <span className="truncate text-xs text-muted-foreground">
              {describeSchedule(habit.schedule, habit.unit)}
            </span>
          )}
        </div>
      </div>
      <LogControl habit={habit} day={habit.today} size="sm" />
    </li>
  )
}

function TodayLists({ dashboard: d }: { dashboard: Dashboard }) {
  return (
    <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <ListTodo className="size-4 text-primary" aria-hidden /> Pendientes hoy
          </CardTitle>
          <span className="text-sm text-muted-foreground">{d.pending.length}</span>
        </CardHeader>
        <CardContent>
          {d.pending.length === 0 ? (
            <p className="py-4 text-sm text-muted-foreground">
              Nada pendiente de los hábitos diarios. ¡Bien hecho!
            </p>
          ) : (
            <ul className="divide-y divide-border">
              {d.pending.map((habit) => (
                <HabitRow key={habit.id} habit={habit} />
              ))}
            </ul>
          )}
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <CircleCheck className="size-4 text-success" aria-hidden /> Completados hoy
          </CardTitle>
          <span className="text-sm text-muted-foreground">{d.completed.length}</span>
        </CardHeader>
        <CardContent>
          {d.completed.length === 0 ? (
            <p className="py-4 text-sm text-muted-foreground">Aún no has completado ninguno hoy.</p>
          ) : (
            <ul className="divide-y divide-border">
              {d.completed.map((habit) => (
                <HabitRow key={habit.id} habit={habit} />
              ))}
            </ul>
          )}
        </CardContent>
      </Card>
      {d.weekly.length > 0 ? (
        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <CalendarRange className="size-4 text-primary" aria-hidden /> Metas semanales
            </CardTitle>
          </CardHeader>
          <CardContent>
            <ul className="divide-y divide-border">
              {d.weekly.map((habit) => (
                <HabitRow key={habit.id} habit={habit} />
              ))}
            </ul>
          </CardContent>
        </Card>
      ) : null}
    </div>
  )
}

function StreakCards({ dashboard: d }: { dashboard: Dashboard }) {
  return (
    <div className="grid grid-cols-2 gap-4 xl:grid-cols-1">
      <StatTile
        label="Racha activa destacada"
        icon={Flame}
        iconClassName="text-streak"
        value={
          d.topCurrentStreak
            ? `${d.topCurrentStreak.length} ${unitLabel(d.topCurrentStreak.unit, d.topCurrentStreak.length)}`
            : '—'
        }
        hint={
          d.topCurrentStreak ? (
            <Link to={`/habitos/${d.topCurrentStreak.habitId}`} className="hover:underline">
              {d.topCurrentStreak.name}
            </Link>
          ) : (
            'Completa un hábito para empezar una racha'
          )
        }
      />
      <StatTile
        label="Mejor racha histórica"
        icon={Trophy}
        value={
          d.bestStreak ? `${d.bestStreak.length} ${unitLabel(d.bestStreak.unit, d.bestStreak.length)}` : '—'
        }
        hint={d.bestStreak?.name ?? 'Tu récord aparecerá aquí'}
      />
    </div>
  )
}

function WeekCard({ dashboard: d }: { dashboard: Dashboard }) {
  const rate = d.week.compliance.combinedRate
  const previous = d.week.previousCombinedRate
  const delta = rate !== null && previous !== null ? Math.round((rate - previous) * 10) / 10 : null
  return (
    <Card>
      <CardHeader>
        <CardTitle>Resumen semanal</CardTitle>
        <span className="text-xs text-muted-foreground">
          {formatDayMonth(d.week.from)} – {formatDayMonth(d.week.to)}
        </span>
      </CardHeader>
      <CardContent>
        {d.week.compliance.expectedOccurrences === 0 ? (
          <p className="mb-2 text-sm text-muted-foreground">
            La semana acaba de empezar: los días se cuentan cuando terminan o cuando completas un hábito.
          </p>
        ) : null}
        <p className="text-3xl font-bold">{formatPercent(rate)}</p>
        <p className="text-sm text-muted-foreground">
          {d.week.compliance.completedOccurrences} de {d.week.compliance.expectedOccurrences} veces previstas
        </p>
        {delta !== null ? (
          <p className={`mt-1 text-sm font-semibold ${delta >= 0 ? 'text-success' : 'text-danger'}`}>
            {delta >= 0 ? '▲' : '▼'} {formatNumber(Math.abs(delta))} puntos vs. la semana pasada
          </p>
        ) : null}
        <p className="mt-2 text-xs text-muted-foreground">
          Combina días programados cerrados y cuotas semanales; lo que está en curso no resta.
        </p>
      </CardContent>
    </Card>
  )
}

function GoalsCard({ dashboard: d }: { dashboard: Dashboard }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Target className="size-4 text-primary" aria-hidden /> Objetivos activos
        </CardTitle>
        <Link to="/objetivos" className="text-sm font-semibold text-primary hover:underline">
          Ver todos
        </Link>
      </CardHeader>
      <CardContent>
        {d.activeGoals.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            Sin objetivos activos.{' '}
            <Link to="/objetivos" className="font-semibold text-primary hover:underline">
              Crea uno
            </Link>
          </p>
        ) : (
          <ul className="space-y-4">
            {d.activeGoals.map((goal) => (
              <li key={goal.id}>
                <div className="mb-1 flex justify-between gap-2 text-sm">
                  <Link to={`/objetivos/${goal.id}`} className="truncate font-medium hover:underline">
                    {goal.name}
                  </Link>
                  <span className="shrink-0 text-muted-foreground">
                    {formatNumber(goal.progress.percent)} %
                  </span>
                </div>
                <ProgressBar
                  value={goal.progress.percent}
                  label={`Progreso de ${goal.name}`}
                  tone={goal.progress.targetReached ? 'success' : 'primary'}
                />
                <p className="mt-1 text-xs text-muted-foreground">
                  {formatNumber(goal.progress.current)} de {formatNumber(goal.targetValue)} {goal.unit}
                  {goal.daysLeft !== null ? ` · ${goal.daysLeft} días restantes` : ''}
                </p>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}

function RemindersCard({ dashboard: d }: { dashboard: Dashboard }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <BellRing className="size-4 text-primary" aria-hidden /> Próximos recordatorios
        </CardTitle>
      </CardHeader>
      <CardContent>
        {d.upcomingReminders.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            No hay recordatorios activos.{' '}
            <Link to="/recordatorios" className="font-semibold text-primary hover:underline">
              Programar uno
            </Link>
          </p>
        ) : (
          <ul className="space-y-2 text-sm">
            {d.upcomingReminders.map((r) => (
              <li key={r.id} className="flex justify-between gap-2">
                <span className="truncate">{r.title}</span>
                <span className="shrink-0 text-muted-foreground">
                  {formatInstant(r.nextFireAt, d.timeZoneId, {
                    dateStyle: undefined,
                    weekday: 'short',
                    hour: '2-digit',
                    minute: '2-digit',
                    timeStyle: undefined,
                  })}
                </span>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}

function describeActivity(a: Activity): string {
  if (a.action === 'deleted') return 'borró el registro'
  if (a.quantity !== null) return `registró ${formatNumber(a.quantity)} ${a.unit ?? ''}`.trim()
  return a.completed ? 'lo completó' : 'lo marcó como no hecho'
}

function ActivityCard({ activity }: { activity: Activity[] }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Actividad reciente</CardTitle>
      </CardHeader>
      <CardContent>
        {activity.length === 0 ? (
          <p className="text-sm text-muted-foreground">Tus registros aparecerán aquí.</p>
        ) : (
          <ul className="space-y-3 text-sm">
            {activity.map((a, i) => (
              <li key={`${a.at}-${i}`} className="flex items-start gap-2">
                <HabitIcon icon={a.icon} color={a.color} size="sm" />
                <div className="min-w-0">
                  <p className="truncate">
                    <strong>{a.habitName}</strong>: {describeActivity(a)}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    Día {formatDayMonth(a.date)} · {formatRelative(a.at)}
                  </p>
                </div>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}

function Onboarding({ onCreate }: { onCreate: () => void }) {
  const steps = [
    {
      icon: Sparkles,
      title: 'Crea tu primer hábito',
      text: 'Algo pequeño y concreto: «Leer 10 páginas», «Beber agua».',
      action: onCreate,
    },
    {
      icon: BellRing,
      title: 'Programa un recordatorio',
      text: 'A la hora en que mejor te encaje, en tu zona horaria.',
      to: '/recordatorios',
    },
    {
      icon: Target,
      title: 'Define un objetivo',
      text: 'Por ejemplo, «Leer 12 libros este año», con progreso real.',
      to: '/objetivos',
    },
  ]
  return (
    <Card className="p-6">
      <h2 className="text-lg font-semibold">Te damos la bienvenida a HabitFlow</h2>
      <p className="mt-1 text-sm text-muted-foreground">
        Tres pasos para empezar. Tu panel se llenará con datos reales a medida que registres tus días.
      </p>
      <ol className="mt-5 grid grid-cols-1 gap-4 md:grid-cols-3">
        {steps.map((step, i) => (
          <li key={step.title} className="rounded-xl border border-border p-4">
            <span className="mb-3 grid size-10 place-items-center rounded-full bg-primary-soft text-primary">
              <step.icon className="size-5" aria-hidden />
            </span>
            <p className="font-semibold">
              {i + 1}. {step.title}
            </p>
            <p className="mt-1 text-sm text-muted-foreground">{step.text}</p>
            {step.action ? (
              <Button className="mt-3" size="sm" onClick={step.action}>
                Crear hábito <ArrowRight />
              </Button>
            ) : (
              <Button className="mt-3" size="sm" variant="outline" asChild>
                <Link to={step.to!}>
                  Ir <ArrowRight />
                </Link>
              </Button>
            )}
          </li>
        ))}
      </ol>
    </Card>
  )
}

function DashboardSkeleton() {
  return (
    <div className="space-y-6" role="status" aria-label="Cargando el panel">
      <Skeleton className="h-10 w-72" />
      <div className="grid grid-cols-1 gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="space-y-6">
          <Skeleton className="h-36" />
          <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
            <Skeleton className="h-64" />
            <Skeleton className="h-64" />
          </div>
        </div>
        <div className="space-y-6">
          <Skeleton className="h-28" />
          <Skeleton className="h-40" />
          <Skeleton className="h-48" />
        </div>
      </div>
    </div>
  )
}
