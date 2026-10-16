import { AlertTriangle, ChevronLeft, ChevronRight, type LucideIcon } from 'lucide-react'
import type { ReactNode } from 'react'
import { errorMessage } from '@/lib/api-client'
import { cn } from '@/lib/utils'
import { Button } from './ui/button'
import { Card, Skeleton } from './ui/display'

export function PageHeader({
  title,
  description,
  actions,
}: {
  title: string
  description?: ReactNode
  actions?: ReactNode
}) {
  return (
    <div className="mb-6 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
      <div className="min-w-0">
        <h1 className="text-2xl font-bold tracking-tight">{title}</h1>
        {description ? <p className="mt-1 text-sm text-muted-foreground">{description}</p> : null}
      </div>
      {actions ? <div className="flex flex-wrap gap-2">{actions}</div> : null}
    </div>
  )
}

export function EmptyState({
  icon: Icon,
  title,
  description,
  action,
  className,
}: {
  icon: LucideIcon
  title: string
  description?: ReactNode
  action?: ReactNode
  className?: string
}) {
  return (
    <div
      className={cn(
        'flex flex-col items-center rounded-xl border border-dashed border-border bg-surface px-6 py-12 text-center',
        className,
      )}
    >
      <span className="mb-3 grid size-12 place-items-center rounded-full bg-primary-soft text-primary">
        <Icon className="size-6" aria-hidden />
      </span>
      <h2 className="text-base font-semibold">{title}</h2>
      {description ? <p className="mt-1 max-w-md text-sm text-muted-foreground">{description}</p> : null}
      {action ? <div className="mt-5">{action}</div> : null}
    </div>
  )
}

export function ErrorState({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  return (
    <div
      role="alert"
      className="flex flex-col items-center rounded-xl border border-danger/30 bg-danger-soft px-6 py-10 text-center"
    >
      <AlertTriangle className="mb-2 size-6 text-danger" aria-hidden />
      <p className="font-semibold text-danger">No se pudo cargar la información</p>
      <p className="mt-1 text-sm text-muted-foreground">{errorMessage(error)}</p>
      {onRetry ? (
        <Button variant="outline" className="mt-4" onClick={onRetry}>
          Reintentar
        </Button>
      ) : null}
    </div>
  )
}

export function ListSkeleton({ rows = 5 }: { rows?: number }) {
  return (
    <Card className="divide-y divide-border">
      {Array.from({ length: rows }, (_, i) => (
        <div key={i} className="flex items-center gap-4 p-4">
          <Skeleton className="size-10 rounded-full" />
          <div className="flex-1 space-y-2">
            <Skeleton className="h-4 w-1/3" />
            <Skeleton className="h-3 w-1/2" />
          </div>
        </div>
      ))}
      <span className="sr-only" role="status">
        Cargando…
      </span>
    </Card>
  )
}

export function Pagination({
  page,
  totalPages,
  totalCount,
  onPageChange,
}: {
  page: number
  totalPages: number
  totalCount: number
  onPageChange: (page: number) => void
}) {
  if (totalPages <= 1) return null
  return (
    <nav aria-label="Paginación" className="mt-4 flex items-center justify-between gap-3 text-sm">
      <span className="text-muted-foreground">
        Página {page} de {totalPages} · {totalCount} resultados
      </span>
      <div className="flex gap-2">
        <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
          <ChevronLeft /> Anterior
        </Button>
        <Button
          variant="outline"
          size="sm"
          disabled={page >= totalPages}
          onClick={() => onPageChange(page + 1)}
        >
          Siguiente <ChevronRight />
        </Button>
      </div>
    </nav>
  )
}
