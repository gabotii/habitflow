import { CalendarOff } from 'lucide-react'
import { Link } from 'react-router'
import { DayStatusBadge, HabitIcon } from '@/components/habit-visuals'
import { ErrorState } from '@/components/page'
import { Card, CardContent, CardHeader, CardTitle, Skeleton } from '@/components/ui/display'
import { LogControl } from '@/features/habit-logs/LogControl'
import { capitalize, formatLongDate } from '@/lib/dates'
import { CALENDAR_STATE } from '@/lib/labels'
import { formatPercent } from '@/lib/utils'
import { useCalendarDay } from './api'

/** The habits of one day with their status; past days within the habit's window can be corrected here. */
export function DayPanel({ date }: { date: string }) {
  const detail = useCalendarDay(date)

  return (
    <Card aria-live="polite">
      <CardHeader>
        <div>
          <CardTitle>{capitalize(formatLongDate(date))}</CardTitle>
          {detail.data ? (
            <p className="text-sm text-muted-foreground">
              {CALENDAR_STATE[detail.data.summary.state].label}
              {detail.data.summary.scheduled > 0
                ? ` · ${detail.data.summary.completed} de ${detail.data.summary.scheduled} (${formatPercent(detail.data.summary.percent)})`
                : ''}
            </p>
          ) : null}
        </div>
      </CardHeader>
      <CardContent>
        {detail.isPending ? (
          <div className="space-y-3">
            <Skeleton className="h-12" />
            <Skeleton className="h-12" />
          </div>
        ) : detail.isError ? (
          <ErrorState error={detail.error} onRetry={() => detail.refetch()} />
        ) : detail.data.habits.length === 0 ? (
          <div className="flex flex-col items-center py-8 text-center text-sm text-muted-foreground">
            <CalendarOff className="mb-2 size-6" aria-hidden />
            {date > detail.data.today
              ? 'No hay hábitos programados para este día.'
              : 'Ningún hábito estaba programado este día.'}
          </div>
        ) : (
          <ul className="divide-y divide-border">
            {detail.data.habits.map(({ habit, day, week, canLog }) => (
              <li key={habit.id} className="flex items-center gap-3 py-3">
                <HabitIcon icon={habit.icon} color={habit.color} size="sm" />
                <div className="min-w-0 flex-1">
                  <Link
                    to={`/habitos/${habit.id}`}
                    className="block truncate text-sm font-semibold hover:underline"
                  >
                    {habit.name}
                  </Link>
                  <div className="mt-0.5 flex flex-wrap items-center gap-2">
                    <DayStatusBadge status={day.status} extra={!day.scheduled} />
                    {week ? (
                      <span className="text-xs text-muted-foreground">
                        Semana {week.completed}/{week.required}
                      </span>
                    ) : null}
                  </div>
                  {day.note ? (
                    <p className="mt-1 truncate text-xs text-muted-foreground">“{day.note}”</p>
                  ) : null}
                </div>
                {canLog ? (
                  <LogControl habit={habit} day={day} isToday={date === detail.data.today} size="sm" />
                ) : null}
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}
