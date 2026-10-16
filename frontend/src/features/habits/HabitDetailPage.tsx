import {
  Archive,
  ArchiveRestore,
  ArrowLeft,
  CalendarCheck,
  Flame,
  History,
  Pencil,
  Percent,
  Trash2,
  Trophy,
} from 'lucide-react'
import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { toast } from 'sonner'
import { DayStatusBadge, HabitIcon, ProgressBar, StreakBadge } from '@/components/habit-visuals'
import { ErrorState, Pagination } from '@/components/page'
import { StatTile } from '@/components/stat-tile'
import { Button } from '@/components/ui/button'
import { Badge, Card, CardContent, CardHeader, CardTitle, Skeleton } from '@/components/ui/display'
import { ConfirmDialog } from '@/components/ui/dialog'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { LogControl } from '@/features/habit-logs/LogControl'
import { LogDialog } from '@/features/habit-logs/LogDialog'
import { ChartCard, ColumnChart, RunningAreaChart, CHART } from '@/features/statistics/charts'
import { errorMessage } from '@/lib/api-client'
import { addLocalDays, formatDayMonth, formatInstant, formatShortDate } from '@/lib/dates'
import { DAY_STATUS, describeSchedule, unitLabel, WEEKDAY_LABEL } from '@/lib/labels'
import { formatNumber, formatPercent } from '@/lib/utils'
import type { Habit, HabitDay, HabitLog, LogChange } from '@/types/api'
import { useHabit, useHabitAction, useHabitChanges, useHabitLogs, useHabitStatistics } from './api'
import { HabitFormDialog } from './HabitFormDialog'

const RANGES = [
  { value: '30', label: '30 días' },
  { value: '90', label: '90 días' },
  { value: '365', label: '1 año' },
]

export function HabitDetailPage() {
  const { id = '' } = useParams()
  const habit = useHabit(id)

  if (habit.isPending) {
    return (
      <div className="space-y-4" role="status" aria-label="Cargando hábito">
        <Skeleton className="h-16 w-1/2" />
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-4">
          {Array.from({ length: 4 }, (_, i) => (
            <Skeleton key={i} className="h-24" />
          ))}
        </div>
        <Skeleton className="h-72" />
      </div>
    )
  }
  if (habit.isError) return <ErrorState error={habit.error} onRetry={() => habit.refetch()} />
  return <HabitDetail habit={habit.data} />
}

function HabitDetail({ habit }: { habit: Habit }) {
  const navigate = useNavigate()
  const action = useHabitAction(habit.id)
  const [editing, setEditing] = useState(false)
  const [confirm, setConfirm] = useState<'archive' | 'delete' | null>(null)
  const [range, setRange] = useState('30')
  const today = habit.today.date
  const from = addLocalDays(today, -(Number(range) - 1))
  const stats = useHabitStatistics(habit.id, from, today)
  const streak = habit.streak
  const unit = streak.unit

  const run = (kind: 'archive' | 'restore' | 'delete') =>
    action.mutate(kind, {
      onSuccess: () => {
        setConfirm(null)
        if (kind === 'delete') {
          toast.success('Hábito eliminado definitivamente')
          navigate('/habitos', { replace: true })
        } else {
          toast.success(
            kind === 'archive' ? 'Hábito archivado. Su historial se conserva.' : 'Hábito restaurado',
          )
        }
      },
      onError: (error) => {
        setConfirm(null)
        toast.error(errorMessage(error))
      },
    })

  return (
    <>
      <Link
        to="/habitos"
        className="mb-4 inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="size-4" aria-hidden /> Hábitos
      </Link>

      <div className="mb-6 flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
        <div className="flex items-start gap-4">
          <HabitIcon icon={habit.icon} color={habit.color} size="lg" />
          <div className="min-w-0">
            <h1 className="text-2xl font-bold tracking-tight">{habit.name}</h1>
            <p className="text-sm text-muted-foreground">
              {describeSchedule(habit.schedule, habit.unit)} · {habit.category.name} · desde{' '}
              {formatShortDate(habit.startDate)}
            </p>
            {habit.description ? <p className="mt-2 max-w-2xl text-sm">{habit.description}</p> : null}
            <div className="mt-2 flex flex-wrap gap-2">
              {habit.isArchived ? (
                <Badge className="bg-muted text-muted-foreground">
                  <Archive aria-hidden /> Archivado
                </Badge>
              ) : (
                <DayStatusBadge status={habit.today.status} extra={!habit.today.scheduled} />
              )}
              <StreakBadge length={streak.current} unit={unit} />
            </div>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {habit.isArchived ? (
            <>
              <Button variant="outline" onClick={() => run('restore')} disabled={action.isPending}>
                <ArchiveRestore /> Restaurar
              </Button>
              <Button variant="danger" onClick={() => setConfirm('delete')}>
                <Trash2 /> Eliminar
              </Button>
            </>
          ) : (
            <>
              <LogControl habit={habit} day={habit.today} />
              <Button variant="outline" onClick={() => setEditing(true)}>
                <Pencil /> Editar
              </Button>
              <Button variant="outline" onClick={() => setConfirm('archive')}>
                <Archive /> Archivar
              </Button>
            </>
          )}
        </div>
      </div>

      <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatTile
          label="Racha actual"
          value={`${streak.current} ${unitLabel(unit, streak.current)}`}
          hint={streak.currentStart ? `Desde el ${formatShortDate(streak.currentStart)}` : 'Empieza hoy'}
          icon={Flame}
          iconClassName="text-streak"
        />
        <StatTile
          label="Mejor racha"
          value={streak.best ? `${streak.best.length} ${unitLabel(unit, streak.best.length)}` : '—'}
          hint={
            streak.best
              ? `${formatDayMonth(streak.best.start)} – ${formatShortDate(streak.best.end)}`
              : 'Aún sin récord'
          }
          icon={Trophy}
        />
        <StatTile
          label="Cumplimiento total"
          value={formatPercent(streak.allTime.combinedRate)}
          hint={`${streak.allTime.completedOccurrences} de ${streak.allTime.expectedOccurrences} veces previstas`}
          icon={Percent}
        />
        <StatTile
          label="Último día completado"
          value={streak.lastCompletedDate ? formatShortDate(streak.lastCompletedDate) : '—'}
          hint={
            streak.allTime.extraCompletions
              ? `${streak.allTime.extraCompletions} días extra fuera de programa`
              : undefined
          }
          icon={CalendarCheck}
        />
      </div>

      {streak.currentWeek ? (
        <Card className="mb-6 p-4">
          <div className="mb-2 flex flex-wrap justify-between gap-2 text-sm">
            <span>
              Esta semana: <strong>{streak.currentWeek.completed}</strong> de {streak.currentWeek.required}
            </span>
            <span className="text-muted-foreground">
              {streak.currentWeek.achieved
                ? 'Cuota semanal cumplida'
                : `Quedan ${streak.currentWeek.daysLeft} ${streak.currentWeek.daysLeft === 1 ? 'día' : 'días'}`}
            </span>
          </div>
          <ProgressBar
            value={(streak.currentWeek.completed / Math.max(1, streak.currentWeek.required)) * 100}
            label="Progreso de la semana"
            tone={streak.currentWeek.achieved ? 'success' : 'primary'}
          />
        </Card>
      ) : null}

      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-lg font-semibold">Estadísticas</h2>
        <Tabs value={range} onValueChange={setRange}>
          <TabsList aria-label="Periodo">
            {RANGES.map((r) => (
              <TabsTrigger key={r.value} value={r.value}>
                {r.label}
              </TabsTrigger>
            ))}
          </TabsList>
        </Tabs>
      </div>

      {stats.isError ? (
        <ErrorState error={stats.error} onRetry={() => stats.refetch()} />
      ) : !stats.data ? (
        <Skeleton className="mb-6 h-72" />
      ) : (
        <div className={stats.isPlaceholderData ? 'opacity-60 transition-opacity' : undefined}>
          <div className="mb-4 grid grid-cols-1 gap-4 sm:grid-cols-3">
            <StatTile
              label={unit === 'week' ? 'Semanas con cuota cumplida' : 'Días programados cumplidos'}
              value={formatPercent(
                unit === 'week' ? stats.data.compliance.weeklyRate : stats.data.compliance.scheduledDayRate,
              )}
              hint={
                unit === 'week'
                  ? `${stats.data.compliance.weeksAchieved} de ${stats.data.compliance.weeks} semanas cerradas`
                  : `${stats.data.compliance.completedScheduledDays} de ${stats.data.compliance.scheduledDays} días`
              }
            />
            <StatTile
              label="Registros completados"
              value={formatPercent(stats.data.compliance.logCompletionRate)}
              hint={`${stats.data.compliance.logsCompleted} de ${stats.data.compliance.logsRecorded} registros`}
            />
            {habit.type === 'quantity' ? (
              <StatTile
                label="Total acumulado"
                value={`${formatNumber(stats.data.compliance.totalQuantity)} ${habit.unit ?? ''}`}
                hint="Suma de las cantidades del periodo"
              />
            ) : (
              <StatTile
                label="Días extra"
                value={stats.data.compliance.extraCompletions}
                hint="Completados en días no programados"
              />
            )}
          </div>
          <div className="mb-6 grid grid-cols-1 gap-4 xl:grid-cols-2">
            <ChartCard
              title="Evolución de la racha"
              subtitle={`Longitud de la racha al final de cada día (${unitLabel(unit, 2)})`}
              rows={stats.data.streakSeries}
              columns={[
                { header: 'Fecha', cell: (r) => formatShortDate(r.date) },
                { header: 'Racha', cell: (r) => `${r.length} ${unitLabel(r.unit, r.length)}`, numeric: true },
              ]}
            >
              <RunningAreaChart
                data={stats.data.streakSeries}
                xKey="date"
                yKey="length"
                name="de racha"
                formatX={formatDayMonth}
                formatValue={(v) => `${v ?? 0}`}
              />
            </ChartCard>
            {habit.type === 'quantity' ? (
              <ChartCard
                title={`Cantidad por día (${habit.unit ?? ''})`}
                subtitle="Total registrado cada día frente a la meta"
                rows={stats.data.quantities}
                legend={[
                  { label: 'Meta cumplida', color: CHART.series },
                  { label: 'Por debajo de la meta', color: CHART.seriesLight },
                ]}
                columns={[
                  { header: 'Fecha', cell: (r) => formatShortDate(r.date) },
                  {
                    header: 'Cantidad',
                    cell: (r) => (r.quantity === null ? '—' : formatNumber(r.quantity)),
                    numeric: true,
                  },
                  {
                    header: 'Meta',
                    cell: (r) => (r.target === null ? '—' : formatNumber(r.target)),
                    numeric: true,
                  },
                ]}
              >
                <ColumnChart
                  data={stats.data.quantities.map((q) => ({ ...q, value: q.quantity ?? 0 }))}
                  xKey="date"
                  yKey="value"
                  name={habit.unit ?? ''}
                  formatX={formatDayMonth}
                  formatValue={(v) => formatNumber(v ?? 0)}
                  emphasize={(row) => row.completed}
                  target={habit.schedule.targetValue}
                />
              </ChartCard>
            ) : (
              <ChartCard
                title="Constancia por día de la semana"
                subtitle={
                  unit === 'week'
                    ? 'Veces completado cada día de la semana'
                    : '% de días programados cumplidos'
                }
                rows={stats.data.weekdays}
                columns={[
                  { header: 'Día', cell: (r) => WEEKDAY_LABEL[r.weekday] },
                  { header: 'Cumplimiento', cell: (r) => formatPercent(r.rate), numeric: true },
                  { header: 'Completados', cell: (r) => r.completions, numeric: true },
                ]}
              >
                <ColumnChart
                  data={stats.data.weekdays.map((w) => ({
                    ...w,
                    label: WEEKDAY_LABEL[w.weekday].slice(0, 3),
                    value: unit === 'week' ? w.completions : (w.rate ?? 0),
                  }))}
                  xKey="label"
                  yKey="value"
                  name={unit === 'week' ? 'veces' : 'cumplido'}
                  formatX={(v) => v}
                  formatValue={(v) => (unit === 'week' ? `${v ?? 0}` : `${formatNumber(v ?? 0)} %`)}
                  percent={unit !== 'week'}
                />
              </ChartCard>
            )}
          </div>
        </div>
      )}

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
        <RecentLogs habit={habit} />
        <ChangeHistory habit={habit} />
      </div>

      <Card className="mt-4">
        <CardHeader>
          <CardTitle>Historial de reglas</CardTitle>
        </CardHeader>
        <CardContent>
          <ol className="space-y-2 text-sm">
            {habit.scheduleHistory.map((version, i) => (
              <li key={version.effectiveFrom} className="flex flex-wrap justify-between gap-2">
                <span>{describeSchedule(version.schedule, habit.unit)}</span>
                <span className="text-muted-foreground">
                  {i === 0 ? 'Desde el inicio' : 'Desde el'} {formatShortDate(version.effectiveFrom)}
                </span>
              </li>
            ))}
            {habit.pauses.map((pause) => (
              <li key={pause.start} className="flex flex-wrap justify-between gap-2 text-muted-foreground">
                <span>Archivado (sin días programados)</span>
                <span>
                  {formatShortDate(pause.start)} – {pause.end ? formatShortDate(pause.end) : 'actualidad'}
                </span>
              </li>
            ))}
          </ol>
          <p className="mt-3 text-xs text-muted-foreground">
            Cada día se evalúa con las reglas vigentes ese día: cambiar la frecuencia no altera tu historial.
            Zona horaria: {habit.effectiveTimeZoneId}.
          </p>
        </CardContent>
      </Card>

      <HabitFormDialog open={editing} onOpenChange={setEditing} habit={habit} />
      <ConfirmDialog
        open={confirm === 'archive'}
        onOpenChange={(open) => !open && setConfirm(null)}
        title="¿Archivar este hábito?"
        description="Dejará de aparecer en tu día a día y no contará como incumplido mientras esté archivado. Su historial se conserva y puedes restaurarlo cuando quieras."
        confirmLabel="Archivar"
        destructive={false}
        pending={action.isPending}
        onConfirm={() => run('archive')}
      />
      <ConfirmDialog
        open={confirm === 'delete'}
        onOpenChange={(open) => !open && setConfirm(null)}
        title="¿Eliminar definitivamente?"
        description="Se borrarán el hábito y todo su historial de registros. Esta acción no se puede deshacer."
        confirmLabel="Eliminar para siempre"
        pending={action.isPending}
        onConfirm={() => run('delete')}
      />
    </>
  )
}

function RecentLogs({ habit }: { habit: Habit }) {
  const logs = useHabitLogs(habit.id)
  const [editing, setEditing] = useState<HabitDay | null>(null)

  const toDay = (log: HabitLog): HabitDay => ({
    date: log.date,
    status: log.completed ? 'completed' : log.quantity ? 'partial' : 'missed',
    scheduled: true,
    quantity: log.quantity,
    completed: log.completed,
    // The list does not carry the target of each past day; the dialog then omits it rather than guess.
    target: null,
    note: log.note,
  })

  return (
    <Card>
      <CardHeader>
        <CardTitle>Registros recientes</CardTitle>
        <span className="text-xs text-muted-foreground">Últimos 90 días</span>
      </CardHeader>
      <CardContent>
        {logs.isPending ? (
          <Skeleton className="h-32" />
        ) : logs.isError ? (
          <ErrorState error={logs.error} onRetry={() => logs.refetch()} />
        ) : logs.data.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            Todavía no hay registros. Marca el hábito para empezar tu racha.
          </p>
        ) : (
          <ul className="max-h-80 divide-y divide-border overflow-y-auto">
            {logs.data.map((log) => {
              const meta = DAY_STATUS[log.completed ? 'completed' : log.quantity ? 'partial' : 'missed']
              const Icon = meta.icon
              return (
                <li key={log.date} className="flex items-center gap-3 py-2 text-sm">
                  <Icon
                    className={`size-4 shrink-0 ${meta.className.split(' ').find((c) => c.startsWith('text-'))}`}
                    aria-label={meta.label}
                  />
                  <span className="w-28 shrink-0">{formatShortDate(log.date)}</span>
                  <span className="flex-1 truncate text-muted-foreground">
                    {log.quantity !== null
                      ? `${formatNumber(log.quantity)} ${habit.unit ?? ''}`
                      : log.completed
                        ? 'Completado'
                        : 'No completado'}
                    {log.note ? ` · ${log.note}` : ''}
                  </span>
                  {!habit.isArchived ? (
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => setEditing(toDay(log))}
                      aria-label={`Corregir ${formatShortDate(log.date)}`}
                    >
                      Corregir
                    </Button>
                  ) : null}
                </li>
              )
            })}
          </ul>
        )}
      </CardContent>
      {editing ? (
        <LogDialog open onOpenChange={(open) => !open && setEditing(null)} habit={habit} day={editing} />
      ) : null}
    </Card>
  )
}

function describeChange(change: LogChange, unit: string | null): string {
  const value = (quantity: number | null, completed: boolean | null) =>
    quantity !== null
      ? `${formatNumber(quantity)} ${unit ?? ''}`.trim()
      : completed
        ? 'completado'
        : 'no completado'
  switch (change.action) {
    case 'created':
      return `Registrado: ${value(change.newQuantity, change.newCompleted)}`
    case 'deleted':
      return `Registro borrado (era ${value(change.previousQuantity, change.previousCompleted)})`
    default:
      return `Corregido: ${value(change.previousQuantity, change.previousCompleted)} → ${value(change.newQuantity, change.newCompleted)}`
  }
}

function ChangeHistory({ habit }: { habit: Habit }) {
  const [page, setPage] = useState(1)
  const changes = useHabitChanges(habit.id, page)
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <History className="size-4 text-muted-foreground" aria-hidden /> Historial de cambios
        </CardTitle>
      </CardHeader>
      <CardContent>
        {changes.isPending ? (
          <Skeleton className="h-32" />
        ) : changes.isError ? (
          <ErrorState error={changes.error} onRetry={() => changes.refetch()} />
        ) : changes.data.items.length === 0 ? (
          <p className="text-sm text-muted-foreground">Sin cambios todavía.</p>
        ) : (
          <>
            <ul className="divide-y divide-border text-sm">
              {changes.data.items.map((change, i) => (
                <li key={`${change.at}-${i}`} className="py-2">
                  <p>{describeChange(change, habit.unit)}</p>
                  <p className="text-xs text-muted-foreground">
                    Día {formatShortDate(change.date)} · {formatInstant(change.at)}
                  </p>
                </li>
              ))}
            </ul>
            <Pagination
              page={changes.data.page}
              totalPages={changes.data.totalPages}
              totalCount={changes.data.totalCount}
              onPageChange={setPage}
            />
          </>
        )}
      </CardContent>
    </Card>
  )
}
