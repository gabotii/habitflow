import type { LucideIcon } from 'lucide-react'
import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'
import { Card } from './ui/display'

/** Stat tile: sentence-case label, a semibold value, optional context line. Text uses text tokens only. */
export function StatTile({
  label,
  value,
  hint,
  icon: Icon,
  iconClassName,
  className,
}: {
  label: string
  value: ReactNode
  hint?: ReactNode
  icon?: LucideIcon
  iconClassName?: string
  className?: string
}) {
  return (
    <Card className={cn('p-4', className)}>
      <div className="flex items-center justify-between gap-2">
        <p className="text-sm text-muted-foreground">{label}</p>
        {Icon ? <Icon className={cn('size-4 text-muted-foreground', iconClassName)} aria-hidden /> : null}
      </div>
      <p className="mt-1 text-2xl font-bold tracking-tight">{value}</p>
      {hint ? <p className="mt-0.5 text-xs text-muted-foreground">{hint}</p> : null}
    </Card>
  )
}
