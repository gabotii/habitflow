import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { BarChart3, CalendarCheck2, CalendarRange, ListChecks, Target } from 'lucide-react'
import { Link, useSearchParams } from 'react-router'
import { HabitIcon, ProgressBar, StreakBadge } from '@/components/habit-visuals'
import { EmptyState, ErrorState, PageHeader } from '@/components/page'
import { StatTile } from '@/components/stat-tile'
import { Card, CardContent, CardHeader, CardTitle, Skeleton } from '@/components/ui/display'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { api } from '@/lib/api-client'
import { formatDayMonth, formatShortDate, parseLocalDate } from '@/lib/dates'
import { WEEKDAY_LABEL } from '@/lib/labels'
import { cn, formatNumber, formatPercent } from '@/lib/utils'
import type { Statistics } from '@/types/api'
import { ChartCard, ColumnChart, RateLineChart } from './charts'

const RANGES = [
  { days: 7, label: '7 días' },
  { days: 30, label: '30 días' },
  { days: 90, label: '90 días' },
  { days: 365, label: '12 meses' },
]

function useStatistics(days: number) {
  return useQuery({
    queryKey: ['statistics', days],
    queryFn: () => api.get<Statistics>('/statistics', { days }),
    placeholderData: keepPreviousData,
  })
}

const monthLabel = (date: string) =>
  new Intl.DateTimeFormat('es-ES', { month: 'short', year: '2-digit' }).format(parseLocalDate(date))

export function StatisticsPage() {
  const [params, setParams] = useSearchParams()
  const days = RANGES.some((r) => String(r.days) === params.get('dias')) ? Number(params.get('dias')) : 30
  const stats = useStatistics(days)

  return (
    <>
      <PageHeader
        title="Estadísticas"
        description="Indicadores separados y con fórmula explícita: nada se mezcla en un único número ambiguo."
        actions={
          <Tabs
            value={String(days)}
            onValueChange={(v) => setParams(v === '30' ? {} : { dias: v }, { replace: true })}
          >
            <TabsList aria-label="Periodo">
              {RANGES.map((r) => (
                <TabsTrigger key={r.days} value={String(r.days)}>
                  {r.label}
                </TabsTrigger>
              ))}
            </TabsList>
          </Tabs>
        }
      />
      {stats.isPending ? (
        <div className="space-y-4" role="status" aria-label="Cargando estadísticas">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
            {Array.from({ length: 4 }, (_, i) => (
              <Skeleton key={i} className="h-28" />
            ))}
          </div>
          <Skeleton className="h-80" />
        </div>
      ) : stats.isError ? (
        <ErrorState error={stats.error} onRetry={() => stats.refetch()} />
      ) : stats.data.habits.length === 0 ? (
        <EmptyState
          icon={BarChart3}
          title="Aún no hay datos que analizar"
          description="Crea un hábito y registra algunos días: aquí verás tu constancia, tendencias y comparativas."
        />
      ) : (
        <StatisticsView stats={stats.data} days={days} refreshing={stats.isPlaceholderData} />
      )}
    </>
  )
}

function StatisticsView({
  stats,
  days,
  refreshing,
}: {
  stats: Statistics
  days: number
  refreshing: boolean
}) {
  const s = stats.summary
  const delta = stats.comparison.combinedRateDelta
  return (
    <div className={cn('space-y-6', refreshing && 'opacity-60 transition-opacity')}>
      <p className="text-sm text-muted-foreground">
        Del {formatShortDate(stats.from)} al {formatShortDate(stats.to)}, comparado con el{' '}
        {formatShortDate(stats.comparison.previousFrom)} – {formatShortDate(stats.comparison.previousTo)}.
      </p>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatTile
          label="Cumplimiento combinado"
          icon={Target}
          value={formatPercent(s.combinedRate)}
          hint={
            <>
              {s.completedOccurrences} de {s.expectedOccurrences} veces previstas
              {delta !== null ? (
                <span className={cn('ml-1 font-semibold', delta >= 0 ? 'text-success' : 'text-danger')}>
                  {delta >= 0 ? '▲' : '▼'} {formatNumber(Math.abs(delta))} pts
                </span>
              ) : null}
            </>
          }
        />
        <StatTile
          label="Días programados cumplidos"
          icon={CalendarCheck2}
          value={formatPercent(s.scheduledDayRate)}
          hint={`${s.completedScheduledDays} de ${s.scheduledDays} días cerrados`}
        />
        <StatTile
          label="Cuotas semanales cumplidas"
          icon={CalendarRange}
          value={formatPercent(s.weeklyRate)}
          hint={s.weeks ? `${s.weeksAchieved} de ${s.weeks} semanas` : 'Sin hábitos semanales en el periodo'}
        />
        <StatTile
          label="Registros completados"
          icon={ListChecks}
          value={formatPercent(s.logCompletionRate)}
          hint={`${s.logsCompleted} de ${s.logsRecorded} registros · ${s.extraCompletions} extra`}
        />
      </div>

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
        {days <= 90 ? (
          <ChartCard
            title="Cumplimiento diario"
            subtitle="% de hábitos diarios programados completados cada día"
            rows={stats.daily}
            columns={[
              { header: 'Fecha', cell: (r) => formatShortDate(r.date) },
              { header: 'Completados', cell: (r) => `${r.completed} de ${r.scheduled}`, numeric: true },
              { header: '%', cell: (r) => formatPercent(r.rate), numeric: true },
            ]}
          >
            <RateLineChart
              data={stats.daily}
              xKey="date"
              yKey="rate"
              name="cumplido"
              formatX={formatDayMonth}
            />
          </ChartCard>
        ) : (
          <ChartCard
            title="Cumplimiento mensual"
            subtitle="Cumplimiento combinado de cada mes"
            rows={stats.monthly}
            columns={[
              { header: 'Mes', cell: (r) => monthLabel(r.start) },
              { header: 'Cumplidas', cell: (r) => `${r.completed} de ${r.expected}`, numeric: true },
              { header: '%', cell: (r) => formatPercent(r.rate), numeric: true },
            ]}
          >
            <ColumnChart
              data={stats.monthly.map((m) => ({ ...m, value: m.rate ?? 0 }))}
              xKey="start"
              yKey="value"
              name="cumplido"
              formatX={monthLabel}
              formatValue={(v) => `${formatNumber(v ?? 0)} %`}
              percent
            />
          </ChartCard>
        )}
        <ChartCard
          title="Cumplimiento semanal"
          subtitle="Semanas de lunes a domingo; incluye cuotas semanales"
          rows={stats.weekly}
          columns={[
            { header: 'Semana', cell: (r) => `${formatDayMonth(r.start)} – ${formatDayMonth(r.end)}` },
            { header: 'Cumplidas', cell: (r) => `${r.completed} de ${r.expected}`, numeric: true },
            { header: '%', cell: (r) => formatPercent(r.rate), numeric: true },
          ]}
        >
          <ColumnChart
            data={stats.weekly.map((w) => ({ ...w, value: w.rate ?? 0 }))}
            xKey="start"
            yKey="value"
            name="cumplido"
            formatX={formatDayMonth}
            formatValue={(v) => `${formatNumber(v ?? 0)} %`}
            percent
          />
        </ChartCard>
        <ChartCard
          title="Días con mayor constancia"
          subtitle="% de días programados cumplidos según el día de la semana"
          rows={stats.weekdays}
          columns={[
            { header: 'Día', cell: (r) => WEEKDAY_LABEL[r.weekday] },
            { header: 'Cumplidos', cell: (r) => `${r.completed} de ${r.scheduled}`, numeric: true },
            { header: '%', cell: (r) => formatPercent(r.rate), numeric: true },
          ]}
        >
          <ColumnChart
            data={stats.weekdays.map((w) => ({
              ...w,
              label: WEEKDAY_LABEL[w.weekday].slice(0, 3),
              value: w.rate ?? 0,
            }))}
            xKey="label"
            yKey="value"
            name="cumplido"
            formatX={(v) => v}
            formatValue={(v) => `${formatNumber(v ?? 0)} %`}
            percent
          />
        </ChartCard>
        <Card>
          <CardHeader>
            <CardTitle>Objetivos</CardTitle>
            <Link to="/objetivos" className="text-sm font-semibold text-primary hover:underline">
              Ver objetivos
            </Link>
          </CardHeader>
          <CardContent>
            <dl className="grid grid-cols-2 gap-4">
              {[
                { label: 'En progreso', value: stats.goals.active },
                { label: 'En pausa', value: stats.goals.paused },
                { label: 'Completados (total)', value: stats.goals.completed },
                { label: 'Completados en el periodo', value: stats.goals.completedInRange },
              ].map((item) => (
                <div key={item.label} className="rounded-lg bg-muted p-3">
                  <dt className="text-xs text-muted-foreground">{item.label}</dt>
                  <dd className="text-2xl font-bold">{item.value}</dd>
                </div>
              ))}
            </dl>
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Distribución por hábito</CardTitle>
        </CardHeader>
        <CardContent className="overflow-x-auto">
          <table className="w-full min-w-[36rem] text-sm">
            <thead className="text-left text-xs text-muted-foreground">
              <tr>
                <th scope="col" className="py-2 pr-3 font-semibold">
                  Hábito
                </th>
                <th scope="col" className="w-48 py-2 pr-3 font-semibold">
                  Cumplimiento
                </th>
                <th scope="col" className="py-2 pr-3 text-right font-semibold">
                  Veces
                </th>
                <th scope="col" className="py-2 pr-3 text-right font-semibold">
                  Cantidad
                </th>
                <th scope="col" className="py-2 font-semibold">
                  Racha
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border tabular-nums">
              {stats.habits.map((h) => (
                <tr key={h.habitId}>
                  <th scope="row" className="py-2 pr-3 text-left font-medium">
                    <Link to={`/habitos/${h.habitId}`} className="flex items-center gap-2 hover:underline">
                      <HabitIcon icon={h.icon} color={h.color} size="sm" />
                      <span className="truncate">
                        {h.name}
                        {h.isArchived ? (
                          <span className="text-xs text-muted-foreground"> (archivado)</span>
                        ) : null}
                      </span>
                    </Link>
                  </th>
                  <td className="py-2 pr-3">
                    <div className="flex items-center gap-2">
                      <ProgressBar
                        value={h.compliance.combinedRate ?? 0}
                        label={`Cumplimiento de ${h.name}`}
                        className="flex-1"
                      />
                      <span className="w-14 text-right">{formatPercent(h.compliance.combinedRate)}</span>
                    </div>
                  </td>
                  <td className="py-2 pr-3 text-right">
                    {h.compliance.completedOccurrences}/{h.compliance.expectedOccurrences}
                  </td>
                  <td className="py-2 pr-3 text-right">
                    {h.type === 'quantity'
                      ? `${formatNumber(h.compliance.totalQuantity)} ${h.unit ?? ''}`
                      : '—'}
                  </td>
                  <td className="py-2">
                    <StreakBadge length={h.streak.current} unit={h.streak.unit} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </CardContent>
      </Card>

      <details className="rounded-xl border border-border bg-surface p-5 text-sm">
        <summary className="cursor-pointer font-semibold">¿Cómo se calculan estos indicadores?</summary>
        <ul className="mt-3 list-disc space-y-1.5 pl-5 text-muted-foreground">
          <li>
            <strong className="text-foreground">Días programados cumplidos</strong>: días programados
            completados ÷ días programados ya cerrados (hoy solo cuenta si ya lo completaste).
          </li>
          <li>
            <strong className="text-foreground">Cuotas semanales</strong>: semanas que alcanzaron su cuota ÷
            semanas cerradas. Las semanas incompletas exigen una cuota proporcional.
          </li>
          <li>
            <strong className="text-foreground">Cumplimiento combinado</strong>: (días cumplidos +
            cumplimientos semanales, sin pasar de la cuota) ÷ (días programados + cuotas semanales).
          </li>
          <li>
            <strong className="text-foreground">Registros completados</strong>: registros que cumplen la meta
            ÷ registros guardados (incluye días no programados).
          </li>
          <li>
            Los días sin hábitos programados, los días en pausa y los días futuros nunca cuentan como fallos.
          </li>
        </ul>
      </details>
    </div>
  )
}
