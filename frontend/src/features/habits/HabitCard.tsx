import { Archive } from 'lucide-react'
import { Link } from 'react-router'
import { DayStatusBadge, HabitIcon, ProgressBar, StreakBadge } from '@/components/habit-visuals'
import { Card } from '@/components/ui/display'
import { LogControl } from '@/features/habit-logs/LogControl'
import { describeSchedule } from '@/lib/labels'
import { cn } from '@/lib/utils'
import type { HabitListItem } from '@/types/api'

/** A habit with today's state, streak and (for weekly quotas) the week's progress, plus one-tap logging. */
export function HabitCard({ habit, className }: { habit: HabitListItem; className?: string }) {
  const week = habit.week
  return (
    <Card className={cn('flex flex-col gap-3 p-4', className)}>
      <div className="flex items-start gap-3">
        <HabitIcon icon={habit.icon} color={habit.color} />
        <div className="min-w-0 flex-1">
          <Link
            to={`/habitos/${habit.id}`}
            className="block truncate font-semibold hover:text-primary hover:underline"
          >
            {habit.name}
          </Link>
          <p className="truncate text-xs text-muted-foreground">
            {describeSchedule(habit.schedule, habit.unit)} · {habit.category.name}
          </p>
        </div>
        {habit.isArchived ? null : <LogControl habit={habit} day={habit.today} />}
      </div>

      {week ? (
        <div>
          <div className="mb-1 flex justify-between text-xs text-muted-foreground">
            <span>
              Esta semana: <strong className="text-foreground">{week.completed}</strong> de {week.required}
            </span>
            <span>{week.achieved ? 'Cuota cumplida' : `Quedan ${week.daysLeft} días`}</span>
          </div>
          <ProgressBar
            value={(week.completed / Math.max(1, week.required)) * 100}
            label={`Progreso semanal de ${habit.name}`}
            tone={week.achieved ? 'success' : 'primary'}
          />
        </div>
      ) : null}

      <div className="flex flex-wrap items-center gap-2">
        {habit.isArchived ? (
          <span className="inline-flex items-center gap-1 text-xs font-semibold text-muted-foreground">
            <Archive className="size-3.5" aria-hidden /> Archivado
          </span>
        ) : (
          <DayStatusBadge status={habit.today.status} extra={!habit.today.scheduled} />
        )}
        <StreakBadge length={habit.streak.current} unit={habit.streak.unit} />
        <span className="text-xs text-muted-foreground">Récord: {habit.streak.best}</span>
      </div>
    </Card>
  )
}
