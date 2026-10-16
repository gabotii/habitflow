import { addDays, addWeeks, startOfMonth, startOfWeek } from 'date-fns'
import { es } from 'date-fns/locale'
import { CalendarDays, ChevronLeft, ChevronRight } from 'lucide-react'
import { createContext, useContext, useMemo, useState } from 'react'
import { DayPicker, type DayButtonProps } from 'react-day-picker'
import { useSearchParams } from 'react-router'
import { HabitIcon } from '@/components/habit-visuals'
import { EmptyState, ErrorState, PageHeader } from '@/components/page'
import { Button } from '@/components/ui/button'
import { Card, Skeleton } from '@/components/ui/display'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { LogDialog } from '@/features/habit-logs/LogDialog'
import {
  browserToday,
  capitalize,
  formatDayMonth,
  formatLongDate,
  formatWeekdayDay,
  isLocalDate,
  parseLocalDate,
  toLocalDate,
} from '@/lib/dates'
import { CALENDAR_STATE, DAY_STATUS } from '@/lib/labels'
import { cn } from '@/lib/utils'
import type { CalendarDay, CalendarHabitRef, HabitDay } from '@/types/api'
import { useCalendarRange, useCalendarWeek } from './api'
import { DayPanel } from './DayPanel'

const DaysContext = createContext<Map<string, CalendarDay>>(new Map())

/** Day cell: number + state icon + "done/scheduled". The state is in the accessible name, not only in colour. */
function DayCell({ day, modifiers, className, ...props }: DayButtonProps) {
  const days = useContext(DaysContext)
  const date = toLocalDate(day.date)
  const info = days.get(date)
  const meta = info ? CALENDAR_STATE[info.state] : null
  const Icon = meta?.icon
  const counts =
    !info || info.scheduled === 0
      ? ''
      : info.state === 'future'
        ? `, ${info.scheduled} programados`
        : `, ${info.completed} de ${info.scheduled}`
  const label = `${capitalize(formatLongDate(date))}${meta ? `: ${meta.label}` : ''}${counts}`
  return (
    <button
      {...props}
      aria-label={label}
      className={cn(
        className,
        'relative flex h-12 w-full flex-col items-center justify-center gap-0.5 rounded-xl border text-sm transition-colors',
        info && info.state !== 'empty' && info.state !== 'future' ? meta?.className : 'bg-surface',
        modifiers.outside && 'opacity-45',
        modifiers.selected
          ? 'border-primary ring-2 ring-primary/30'
          : 'border-transparent hover:border-border',
        modifiers.today && 'font-extrabold underline decoration-2 underline-offset-4',
      )}
    >
      <span>{day.date.getDate()}</span>
      {Icon && info && info.state !== 'empty' ? (
        <span className="flex items-center gap-0.5 text-[10px] leading-none font-semibold">
          <Icon className="size-3" aria-hidden />
          {info.scheduled > 0
            ? info.state === 'future'
              ? info.scheduled
              : `${info.completed}/${info.scheduled}`
            : null}
        </span>
      ) : null}
    </button>
  )
}

function Legend() {
  const order = ['complete', 'partial', 'missed', 'pending', 'future', 'empty'] as const
  return (
    <ul className="mt-4 flex flex-wrap gap-x-4 gap-y-2 text-xs text-muted-foreground" aria-label="Leyenda">
      {order.map((state) => {
        const meta = CALENDAR_STATE[state]
        const Icon = meta.icon
        return (
          <li key={state} className="flex items-center gap-1.5">
            <span className={cn('grid size-5 place-items-center rounded-md', meta.className)}>
              <Icon className="size-3.5" aria-hidden />
            </span>
            {meta.label}
          </li>
        )
      })}
    </ul>
  )
}

export function CalendarPage() {
  const [params, setParams] = useSearchParams()
  const view = params.get('vista') === 'semana' ? 'week' : 'month'
  const dateParam = params.get('fecha')
  const selected = dateParam && isLocalDate(dateParam) ? dateParam : browserToday()

  const setState = (changes: { vista?: string; fecha?: string }) => {
    const next = new URLSearchParams(params)
    if (changes.vista !== undefined) {
      if (changes.vista === 'month') next.delete('vista')
      else next.set('vista', 'semana')
    }
    if (changes.fecha) next.set('fecha', changes.fecha)
    setParams(next, { replace: true })
  }

  return (
    <>
      <PageHeader
        title="Calendario"
        description="Tu historial día a día. Elige un día para registrar o corregir tu progreso."
        actions={
          <Tabs value={view} onValueChange={(v) => setState({ vista: v })}>
            <TabsList aria-label="Vista">
              <TabsTrigger value="month">Mes</TabsTrigger>
              <TabsTrigger value="week">Semana</TabsTrigger>
            </TabsList>
          </Tabs>
        }
      />
      {view === 'month' ? (
        <MonthView selected={selected} onSelect={(fecha) => setState({ fecha })} />
      ) : (
        <WeekView date={selected} onDateChange={(fecha) => setState({ fecha })} />
      )}
    </>
  )
}

function MonthView({ selected, onSelect }: { selected: string; onSelect: (date: string) => void }) {
  const [month, setMonth] = useState(() => startOfMonth(parseLocalDate(selected)))
  // Always six weeks (42 days), Monday first: exactly what the grid shows and what the API allows.
  const gridStart = startOfWeek(startOfMonth(month), { weekStartsOn: 1 })
  const from = toLocalDate(gridStart)
  const to = toLocalDate(addDays(gridStart, 41))
  const range = useCalendarRange(from, to)
  const days = useMemo(() => new Map(range.data?.days.map((d) => [d.date, d]) ?? []), [range.data])

  return (
    <div className="grid grid-cols-1 gap-6 lg:grid-cols-[minmax(0,1fr)_24rem]">
      <Card className="p-4 sm:p-5">
        {range.isError ? (
          <ErrorState error={range.error} onRetry={() => range.refetch()} />
        ) : (
          <DaysContext.Provider value={days}>
            <div
              className={
                range.isFetching && range.isPlaceholderData ? 'opacity-60 transition-opacity' : undefined
              }
            >
              <DayPicker
                mode="single"
                required
                locale={es}
                weekStartsOn={1}
                showOutsideDays
                fixedWeeks
                month={month}
                onMonthChange={setMonth}
                selected={parseLocalDate(selected)}
                onSelect={(date) => date && onSelect(toLocalDate(date))}
                components={{ DayButton: DayCell }}
                labels={{
                  labelNext: () => 'Mes siguiente',
                  labelPrevious: () => 'Mes anterior',
                }}
              />
            </div>
          </DaysContext.Provider>
        )}
        <div className="mt-2 flex justify-end">
          <Button
            variant="outline"
            size="sm"
            onClick={() => {
              const today = browserToday()
              setMonth(startOfMonth(parseLocalDate(today)))
              onSelect(today)
            }}
          >
            Hoy
          </Button>
        </div>
        <Legend />
      </Card>
      <DayPanel date={selected} />
    </div>
  )
}

function WeekView({ date, onDateChange }: { date: string; onDateChange: (date: string) => void }) {
  const week = useCalendarWeek(date)
  const [editing, setEditing] = useState<{ habit: CalendarHabitRef; day: HabitDay } | null>(null)
  const move = (weeks: number) => onDateChange(toLocalDate(addWeeks(parseLocalDate(date), weeks)))
  const title = week.data
    ? `${formatDayMonth(week.data.from)} – ${formatDayMonth(week.data.to)}`
    : capitalize(formatLongDate(date))

  return (
    <Card className="p-4 sm:p-5">
      <div className="mb-4 flex items-center justify-between gap-2">
        <Button variant="outline" size="icon" aria-label="Semana anterior" onClick={() => move(-1)}>
          <ChevronLeft />
        </Button>
        <h2 className="text-base font-bold" aria-live="polite">
          {title}
        </h2>
        <div className="flex gap-2">
          <Button variant="outline" size="sm" onClick={() => onDateChange(browserToday())}>
            Hoy
          </Button>
          <Button variant="outline" size="icon" aria-label="Semana siguiente" onClick={() => move(1)}>
            <ChevronRight />
          </Button>
        </div>
      </div>

      {week.isPending ? (
        <Skeleton className="h-64" />
      ) : week.isError ? (
        <ErrorState error={week.error} onRetry={() => week.refetch()} />
      ) : week.data.habits.length === 0 ? (
        <EmptyState
          icon={CalendarDays}
          title="Sin hábitos esta semana"
          description="No había hábitos activos en estos días."
        />
      ) : (
        <div className={cn('overflow-x-auto', week.isPlaceholderData && 'opacity-60')}>
          <table className="w-full min-w-[40rem] border-separate border-spacing-1 text-sm">
            <caption className="sr-only">Hábitos de la semana del {title}</caption>
            <thead>
              <tr>
                <th scope="col" className="w-48 text-left text-xs font-semibold text-muted-foreground">
                  Hábito
                </th>
                {week.data.days.map((d) => (
                  <th
                    key={d.date}
                    scope="col"
                    className={cn(
                      'rounded-lg px-1 py-1 text-center text-xs font-semibold capitalize',
                      d.date === week.data.today ? 'bg-primary-soft text-primary' : 'text-muted-foreground',
                    )}
                  >
                    {formatWeekdayDay(d.date)}
                    {d.scheduled > 0 && d.date <= week.data.today ? (
                      <span className="block font-normal">
                        {d.completed}/{d.scheduled}
                      </span>
                    ) : null}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {week.data.habits.map((row) => (
                <tr key={row.habit.id}>
                  <th scope="row" className="text-left font-medium">
                    <span className="flex items-center gap-2">
                      <HabitIcon icon={row.habit.icon} color={row.habit.color} size="sm" />
                      <span className="min-w-0">
                        <span className="block truncate">{row.habit.name}</span>
                        {row.week ? (
                          <span className="block text-xs font-normal text-muted-foreground">
                            Semana {row.week.completed}/{row.week.required}
                          </span>
                        ) : null}
                      </span>
                    </span>
                  </th>
                  {row.days.map((day) => {
                    const meta = DAY_STATUS[day.status]
                    const Icon = meta.icon
                    const loggable =
                      row.loggableFrom !== null && day.date >= row.loggableFrom && day.date <= row.today
                    return (
                      <td key={day.date} className="text-center">
                        <button
                          type="button"
                          disabled={!loggable}
                          onClick={() => setEditing({ habit: row.habit, day })}
                          aria-label={`${row.habit.name}, ${formatLongDate(day.date)}: ${meta.label}${
                            day.quantity !== null ? `, ${day.quantity} ${row.habit.unit ?? ''}` : ''
                          }`}
                          className={cn(
                            'mx-auto grid size-10 place-items-center rounded-lg transition-colors enabled:hover:ring-2 enabled:hover:ring-primary/30 disabled:cursor-default',
                            day.status === 'notScheduled' && !day.completed
                              ? 'text-muted-foreground/40'
                              : meta.className,
                          )}
                        >
                          <Icon className="size-4" aria-hidden />
                        </button>
                      </td>
                    )
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {editing ? (
        <LogDialog
          open
          onOpenChange={(open) => !open && setEditing(null)}
          habit={{
            id: editing.habit.id,
            name: editing.habit.name,
            type: editing.habit.type,
            unit: editing.habit.unit,
          }}
          day={editing.day}
        />
      ) : null}
      <p className="mt-4 text-xs text-muted-foreground">
        La semana va de lunes a domingo. Los hábitos con meta semanal se cumplen por semana, no por día.
      </p>
    </Card>
  )
}
