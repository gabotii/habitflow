import { createContext, useContext } from 'react'

export interface FieldContextValue {
  id: string
  describedBy: string | undefined
  invalid: boolean
  required: boolean
}

export const FieldContext = createContext<FieldContextValue | null>(null)

/** ARIA wiring for a control rendered inside a FormField; explicit props on the control win. */
export function useFieldAttributes() {
  const field = useContext(FieldContext)
  if (!field) return {}
  return {
    id: field.id,
    'aria-describedby': field.describedBy,
    'aria-invalid': field.invalid || undefined,
    'aria-required': field.required || undefined,
  }
}
