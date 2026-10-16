import { useId, type ReactNode } from 'react'
import { cn } from '@/lib/utils'
import { FieldContext } from './ui/field-context'
import { Label } from './ui/form-controls'

interface FormFieldProps {
  label: string
  error?: string
  hint?: ReactNode
  required?: boolean
  className?: string
  children: ReactNode
}

/**
 * Label + control + hint + error. The nested Input/Select/Textarea picks up the id and ARIA attributes from
 * context, so it also works when the control is wrapped (e.g. in a react-hook-form Controller).
 */
export function FormField({ label, error, hint, required, className, children }: FormFieldProps) {
  const id = useId()
  const hintId = hint ? `${id}-hint` : undefined
  const errorId = error ? `${id}-error` : undefined
  const describedBy = [hintId, errorId].filter(Boolean).join(' ') || undefined

  return (
    <div className={cn('flex flex-col gap-1.5', className)}>
      <Label htmlFor={id}>
        {label}
        {required ? (
          <span className="text-danger" aria-hidden>
            {' '}
            *
          </span>
        ) : null}
      </Label>
      <FieldContext.Provider
        value={{ id, describedBy, invalid: Boolean(error), required: Boolean(required) }}
      >
        {children}
      </FieldContext.Provider>
      {hint ? (
        <p id={hintId} className="text-xs text-muted-foreground">
          {hint}
        </p>
      ) : null}
      {error ? (
        <p id={errorId} role="alert" className="text-xs font-medium text-danger">
          {error}
        </p>
      ) : null}
    </div>
  )
}
