import type { ComponentProps } from 'react'
import { cn } from '@/lib/utils'

export function Card({ className, ...props }: ComponentProps<'div'>) {
  return <div className={cn('rounded-xl border border-border bg-surface shadow-xs', className)} {...props} />
}

export function CardHeader({ className, ...props }: ComponentProps<'div'>) {
  return <div className={cn('flex items-start justify-between gap-3 px-5 pt-5', className)} {...props} />
}

export function CardTitle({ className, ...props }: ComponentProps<'h2'>) {
  return <h2 className={cn('text-base font-semibold', className)} {...props} />
}

export function CardContent({ className, ...props }: ComponentProps<'div'>) {
  return <div className={cn('px-5 pt-3 pb-5', className)} {...props} />
}

export function Badge({ className, ...props }: ComponentProps<'span'>) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold whitespace-nowrap [&_svg]:size-3.5',
        className,
      )}
      {...props}
    />
  )
}

export function Skeleton({ className, ...props }: ComponentProps<'div'>) {
  return <div aria-hidden className={cn('animate-pulse rounded-lg bg-muted', className)} {...props} />
}

export function Spinner({ className, label = 'Cargando…' }: { className?: string; label?: string }) {
  return (
    <span
      role="status"
      className={cn('inline-flex items-center gap-2 text-sm text-muted-foreground', className)}
    >
      <span className="size-4 animate-spin rounded-full border-2 border-current border-t-transparent" />
      <span className="sr-only">{label}</span>
    </span>
  )
}
