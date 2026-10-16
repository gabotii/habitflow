import { Check, Minus, PencilLine, Plus } from 'lucide-react'
import { useState } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { useLogHabit } from '@/features/habits/api'
import { errorMessage } from '@/lib/api-client'
import { cn, formatNumber } from '@/lib/utils'
import type { HabitDay, HabitDayStatus, HabitType } from '@/types/api'
import { LogDialog } from './LogDialog'

export interface LoggableHabit {
  id: string
  name: string
  type: HabitType
  unit: string | null
}

/** Status the server will most likely answer with; only used for the optimistic update. */
export function expectedStatus(
  day: HabitDay,
  completed: boolean,
  quantity: number | null,
  isToday: boolean,
): HabitDayStatus {
  if (completed) return 'completed'
  if (quantity && quantity > 0) return 'partial'
  if (!day.scheduled) return 'notScheduled'
  return isToday ? 'pending' : 'missed'
}

export function quantityStep(target: number | null): number {
  if (target === null) return 1
  if (target >= 30) return 5
  if (target > 5) return 1
  return 0.25
}

/**
 * One-tap logging for a habit on one day. Binary habits toggle; quantity habits step by a sensible amount and
 * always send the resulting daily total (absolute value, so retries are harmless). Exact values and notes go
 * through the dialog.
 */
export function LogControl({
  habit,
  day,
  canLog = true,
  isToday = true,
  size = 'md',
}: {
  habit: LoggableHabit
  day: HabitDay
  canLog?: boolean
  isToday?: boolean
  size?: 'md' | 'sm'
}) {
  const logHabit = useLogHabit()
  const [dialogOpen, setDialogOpen] = useState(false)

  const save = (completed: boolean, quantity: number | null) => {
    const target = day.target
    const done =
      habit.type === 'binary' ? completed : target !== null ? (quantity ?? 0) >= target : (quantity ?? 0) > 0
    logHabit.mutate(
      {
        habitId: habit.id,
        date: day.date,
        input: habit.type === 'binary' ? { completed } : { quantity },
        optimistic: { completed: done, quantity, status: expectedStatus(day, done, quantity, isToday) },
      },
      { onError: (error) => toast.error(errorMessage(error)) },
    )
  }

  const dialog = <LogDialog open={dialogOpen} onOpenChange={setDialogOpen} habit={habit} day={day} />

  if (habit.type === 'binary') {
    return (
      <div className="flex items-center gap-1">
        <button
          type="button"
          disabled={!canLog}
          aria-pressed={day.completed}
          aria-label={day.completed ? `Desmarcar «${habit.name}»` : `Marcar «${habit.name}» como completado`}
          onClick={() => save(!day.completed, null)}
          className={cn(
            'grid shrink-0 place-items-center rounded-full border-2 transition-colors disabled:cursor-not-allowed disabled:opacity-50',
            size === 'md' ? 'size-11' : 'size-9',
            day.completed
              ? 'border-success bg-success text-success-foreground hover:bg-success/90'
              : 'border-border bg-surface text-transparent hover:border-success hover:text-success/60',
          )}
        >
          <Check className={size === 'md' ? 'size-6' : 'size-5'} strokeWidth={3} aria-hidden />
        </button>
        <Button
          variant="ghost"
          size="icon"
          disabled={!canLog}
          aria-label={`Añadir nota o corregir «${habit.name}»`}
          onClick={() => setDialogOpen(true)}
        >
          <PencilLine />
        </Button>
        {dialog}
      </div>
    )
  }

  const quantity = day.quantity ?? 0
  const step = quantityStep(day.target)
  return (
    <div className="flex items-center gap-1">
      <Button
        variant="outline"
        size="icon"
        disabled={!canLog || quantity <= 0}
        aria-label={`Restar ${formatNumber(step)} ${habit.unit ?? ''} a «${habit.name}»`}
        onClick={() => save(false, Math.max(0, round(quantity - step)))}
      >
        <Minus />
      </Button>
      <button
        type="button"
        disabled={!canLog}
        onClick={() => setDialogOpen(true)}
        className="min-w-16 rounded-lg px-2 py-1 text-center text-sm leading-tight hover:bg-muted disabled:cursor-not-allowed"
        aria-label={`Editar la cantidad de «${habit.name}»: ${formatNumber(quantity)}${day.target ? ` de ${formatNumber(day.target)}` : ''} ${habit.unit ?? ''}`}
      >
        <span className="font-bold">{formatNumber(quantity)}</span>
        {day.target ? <span className="text-muted-foreground">/{formatNumber(day.target)}</span> : null}
        <span className="block text-xs text-muted-foreground">{habit.unit}</span>
      </button>
      <Button
        variant="outline"
        size="icon"
        disabled={!canLog}
        aria-label={`Sumar ${formatNumber(step)} ${habit.unit ?? ''} a «${habit.name}»`}
        onClick={() => save(false, round(quantity + step))}
      >
        <Plus />
      </Button>
      {dialog}
    </div>
  )
}

function round(value: number): number {
  return Math.round(value * 100) / 100
}
