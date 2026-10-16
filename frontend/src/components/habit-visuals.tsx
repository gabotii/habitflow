import { CircleCheck, Flame } from 'lucide-react'
import { DAY_STATUS, HABIT_COLOR, HABIT_ICON, unitLabel } from '@/lib/labels'
import { cn, formatNumber } from '@/lib/utils'
import type { HabitColor, HabitDayStatus, PeriodUnit } from '@/types/api'
import { Badge } from './ui/display'

export function HabitIcon({
  icon,
  color,
  size = 'md',
  className,
}: {
  icon: string
  color: HabitColor
  size?: 'sm' | 'md' | 'lg'
  className?: string
}) {
  const Icon = HABIT_ICON[icon]?.icon ?? CircleCheck
  return (
    <span
      aria-hidden
      className={cn(
        'grid shrink-0 place-items-center rounded-xl',
        HABIT_COLOR[color]?.chip ?? HABIT_COLOR.slate.chip,
        { sm: 'size-8 [&_svg]:size-4', md: 'size-10 [&_svg]:size-5', lg: 'size-12 [&_svg]:size-6' }[size],
        className,
      )}
    >
      <Icon />
    </span>
  )
}

export function DayStatusBadge({ status, extra = false }: { status: HabitDayStatus; extra?: boolean }) {
  const meta = DAY_STATUS[status]
  const Icon = meta.icon
  return (
    <Badge className={meta.className}>
      <Icon aria-hidden />
      {extra && status === 'completed' ? 'Extra' : meta.label}
    </Badge>
  )
}

/** Current streak with its unit, e.g. "🔥 5 días" rendered with an icon (never colour alone). */
export function StreakBadge({
  length,
  unit,
  className,
}: {
  length: number
  unit: PeriodUnit
  className?: string
}) {
  return (
    <Badge
      className={cn(
        length > 0 ? 'bg-streak-soft text-streak-ink' : 'bg-muted text-muted-foreground',
        className,
      )}
      title="Racha actual"
    >
      <Flame aria-hidden className={length > 0 ? 'text-streak' : undefined} />
      {length} {unitLabel(unit, length)}
    </Badge>
  )
}

/**
 * Accessible meter. The bar never exceeds 100 %; a surplus over the target is stated in text by the caller.
 */
export function ProgressBar({
  value,
  label,
  tone = 'primary',
  className,
}: {
  value: number
  label: string
  tone?: 'primary' | 'success' | 'warning'
  className?: string
}) {
  const clamped = Math.max(0, Math.min(100, value))
  return (
    <div
      role="progressbar"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(clamped)}
      aria-valuetext={`${formatNumber(clamped)} %`}
      className={cn(
        'h-2 w-full overflow-hidden rounded-full',
        { primary: 'bg-primary-soft', success: 'bg-success-soft', warning: 'bg-warning-soft' }[tone],
        className,
      )}
    >
      <div
        className={cn(
          'h-full rounded-full transition-[width] duration-500',
          { primary: 'bg-primary', success: 'bg-success', warning: 'bg-warning' }[tone],
        )}
        style={{ width: `${clamped}%` }}
      />
    </div>
  )
}

/** Circular progress ring for the dashboard hero. */
export function ProgressRing({ percent, size = 112 }: { percent: number | null; size?: number }) {
  const stroke = 10
  const radius = (size - stroke) / 2
  const circumference = 2 * Math.PI * radius
  const value = Math.max(0, Math.min(100, percent ?? 0))
  return (
    <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} aria-hidden className="-rotate-90">
      <circle
        cx={size / 2}
        cy={size / 2}
        r={radius}
        fill="none"
        strokeWidth={stroke}
        className="stroke-primary-soft"
      />
      <circle
        cx={size / 2}
        cy={size / 2}
        r={radius}
        fill="none"
        strokeWidth={stroke}
        strokeLinecap="round"
        strokeDasharray={circumference}
        strokeDashoffset={circumference * (1 - value / 100)}
        className={cn(
          'transition-[stroke-dashoffset] duration-700',
          value >= 100 ? 'stroke-success' : 'stroke-primary',
        )}
      />
    </svg>
  )
}
