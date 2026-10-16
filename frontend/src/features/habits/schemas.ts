import { z } from 'zod'
import { isLocalDate } from '@/lib/dates'
import type { CreateHabitInput, HabitColor, HabitType, ScheduleKind, Weekday } from '@/types/api'

const decimal = (message: string) =>
  z
    .string()
    .trim()
    .refine((v) => v === '' || /^\d+([.,]\d{1,2})?$/.test(v), { message })

/**
 * Form-level validation mirroring the API rules (the server validates again and is the source of truth).
 * Numbers are strings in the form so empty inputs stay empty.
 */
export const habitFormSchema = z
  .object({
    name: z.string().trim().min(1, 'Ponle un nombre al hábito.').max(80, 'Máximo 80 caracteres.'),
    description: z.string().max(500, 'Máximo 500 caracteres.'),
    categoryId: z.string().min(1, 'Elige una categoría.'),
    color: z.custom<HabitColor>((v) => typeof v === 'string' && v.length > 0),
    icon: z.string().min(1),
    type: z.enum(['binary', 'quantity'] satisfies HabitType[]),
    unit: z.string().trim().max(24, 'Máximo 24 caracteres.'),
    targetValue: decimal('Usa un número con 2 decimales como máximo.'),
    kind: z.enum(['daily', 'weekdays', 'timesPerWeek', 'everyNDays'] satisfies ScheduleKind[]),
    weekdays: z.array(z.custom<Weekday>()),
    timesPerWeek: z.string(),
    intervalDays: z.string(),
    startDate: z.string().refine(isLocalDate, 'Fecha no válida.'),
    timeZoneId: z.string(),
  })
  .superRefine((v, ctx) => {
    if (v.type === 'quantity' && !v.unit) {
      ctx.addIssue({
        code: 'custom',
        path: ['unit'],
        message: 'Indica la unidad (minutos, páginas, litros…).',
      })
    }
    if (v.type === 'quantity' && v.targetValue !== '') {
      const target = Number(v.targetValue.replace(',', '.'))
      if (!(target > 0) || target > 1_000_000) {
        ctx.addIssue({ code: 'custom', path: ['targetValue'], message: 'La meta debe ser mayor que 0.' })
      }
    }
    if (v.kind === 'weekdays' && v.weekdays.length === 0) {
      ctx.addIssue({ code: 'custom', path: ['weekdays'], message: 'Elige al menos un día.' })
    }
    const times = Number(v.timesPerWeek)
    if (v.kind === 'timesPerWeek' && !(Number.isInteger(times) && times >= 1 && times <= 7)) {
      ctx.addIssue({ code: 'custom', path: ['timesPerWeek'], message: 'Entre 1 y 7 veces por semana.' })
    }
    const interval = Number(v.intervalDays)
    if (v.kind === 'everyNDays' && !(Number.isInteger(interval) && interval >= 2 && interval <= 365)) {
      ctx.addIssue({ code: 'custom', path: ['intervalDays'], message: 'Entre 2 y 365 días.' })
    }
  })

export type HabitFormValues = z.infer<typeof habitFormSchema>

/** Maps form values to the API contract (only the parameters of the chosen frequency are sent). */
export function toHabitInput(v: HabitFormValues): CreateHabitInput {
  const target =
    v.type === 'quantity' && v.targetValue !== '' ? Number(v.targetValue.replace(',', '.')) : null
  return {
    name: v.name.trim(),
    description: v.description.trim() || null,
    categoryId: v.categoryId,
    color: v.color,
    icon: v.icon,
    type: v.type,
    unit: v.type === 'quantity' ? v.unit.trim() : null,
    startDate: v.startDate,
    timeZoneId: v.timeZoneId || null,
    schedule: {
      kind: v.kind,
      weekdays: v.kind === 'weekdays' ? v.weekdays : null,
      timesPerWeek: v.kind === 'timesPerWeek' ? Number(v.timesPerWeek) : null,
      intervalDays: v.kind === 'everyNDays' ? Number(v.intervalDays) : null,
      targetValue: target,
    },
  }
}
