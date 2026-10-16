import type { ComponentProps } from 'react'
import { cn } from '@/lib/utils'
import { useFieldAttributes } from './field-context'

const fieldBase =
  'w-full rounded-lg border border-border bg-surface px-3 text-sm text-foreground shadow-xs transition-colors placeholder:text-muted-foreground/70 focus-visible:border-ring focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-ring/20 disabled:cursor-not-allowed disabled:opacity-60 aria-invalid:border-danger aria-invalid:ring-danger/20'

export function Input({ className, ...props }: ComponentProps<'input'>) {
  return <input {...useFieldAttributes()} className={cn(fieldBase, 'h-10', className)} {...props} />
}

export function Textarea({ className, ...props }: ComponentProps<'textarea'>) {
  return (
    <textarea {...useFieldAttributes()} className={cn(fieldBase, 'min-h-24 py-2', className)} {...props} />
  )
}

/** Native select: accessible by default and works well on mobile. */
export function Select({ className, ...props }: ComponentProps<'select'>) {
  return <select {...useFieldAttributes()} className={cn(fieldBase, 'h-10 pr-8', className)} {...props} />
}

export function Label({ className, ...props }: ComponentProps<'label'>) {
  return <label className={cn('text-sm font-medium text-foreground', className)} {...props} />
}

export function Checkbox({ className, ...props }: Omit<ComponentProps<'input'>, 'type'>) {
  return (
    <input
      type="checkbox"
      {...useFieldAttributes()}
      className={cn('size-4 rounded border-border accent-primary', className)}
      {...props}
    />
  )
}
