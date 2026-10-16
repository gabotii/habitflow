import { AlertTriangle, CircleCheck } from 'lucide-react'
import { ProgressBar } from '@/components/habit-visuals'
import { formatNumber } from '@/lib/utils'
import type { Goal } from '@/types/api'

/**
 * Progress exactly as the server computed it. The bar stops at 100 %; a surplus is stated in words instead of
 * overflowing the bar.
 */
export function GoalProgressView({ goal, compact = false }: { goal: Goal; compact?: boolean }) {
  const { progress } = goal
  return (
    <div>
      <div className="mb-1.5 flex items-baseline justify-between gap-2">
        <span className={compact ? 'text-sm' : 'text-lg'}>
          <strong>{formatNumber(progress.current)}</strong>
          <span className="text-muted-foreground">
            {' '}
            / {formatNumber(goal.targetValue)} {goal.unit}
          </span>
        </span>
        <span className="text-sm font-semibold">{formatNumber(progress.percent)} %</span>
      </div>
      <ProgressBar
        value={progress.percent}
        label={`Progreso de ${goal.name}`}
        tone={progress.targetReached ? 'success' : goal.isOverdue ? 'warning' : 'primary'}
      />
      <div className="mt-1.5 flex flex-wrap gap-x-3 gap-y-1 text-xs text-muted-foreground">
        {progress.targetReached ? (
          <span className="inline-flex items-center gap-1 font-semibold text-success">
            <CircleCheck className="size-3.5" aria-hidden />
            Meta alcanzada
            {progress.exceededBy > 0
              ? ` · +${formatNumber(progress.exceededBy)} ${goal.unit} sobre la meta`
              : ''}
          </span>
        ) : goal.isOverdue ? (
          <span className="inline-flex items-center gap-1 font-semibold text-warning">
            <AlertTriangle className="size-3.5" aria-hidden /> Fecha límite superada
          </span>
        ) : goal.daysLeft !== null ? (
          <span>
            {goal.daysLeft === 0
              ? 'Último día'
              : `${goal.daysLeft} ${goal.daysLeft === 1 ? 'día restante' : 'días restantes'}`}
          </span>
        ) : null}
      </div>
    </div>
  )
}
