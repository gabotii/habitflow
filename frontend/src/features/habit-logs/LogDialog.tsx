import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormField } from '@/components/form-field'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent } from '@/components/ui/dialog'
import { Input, Textarea } from '@/components/ui/form-controls'
import { useLogHabit } from '@/features/habits/api'
import { errorMessage } from '@/lib/api-client'
import { capitalize, formatLongDate } from '@/lib/dates'
import { cn } from '@/lib/utils'
import type { HabitDay } from '@/types/api'
import type { LoggableHabit } from './LogControl'

const schema = z.object({
  completed: z.boolean(),
  quantity: z
    .string()
    .trim()
    .refine(
      (v) => v === '' || (!Number.isNaN(Number(v.replace(',', '.'))) && Number(v.replace(',', '.')) >= 0),
      {
        message: 'Introduce un número mayor o igual que 0.',
      },
    )
    .refine((v) => v === '' || Number(v.replace(',', '.')) <= 1_000_000, { message: 'Máximo 1.000.000.' }),
  note: z.string().max(500, 'Máximo 500 caracteres.'),
})

type FormValues = z.infer<typeof schema>

/** Exact value and note for one day (also used to correct past days). */
export function LogDialog({
  open,
  onOpenChange,
  habit,
  day,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  habit: LoggableHabit
  day: HabitDay
}) {
  const logHabit = useLogHabit()
  const {
    register,
    control,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) })

  useEffect(() => {
    if (open) {
      reset({
        completed: day.completed,
        quantity: day.quantity === null ? '' : String(day.quantity),
        note: day.note ?? '',
      })
    }
  }, [open, day, reset])

  const hasLog = day.completed || day.quantity !== null || day.note !== null

  const onSubmit = handleSubmit((values) => {
    const quantity =
      values.quantity === '' ? 0 : Math.round(Number(values.quantity.replace(',', '.')) * 100) / 100
    logHabit.mutate(
      {
        habitId: habit.id,
        date: day.date,
        // An empty note clears it on the server ("" ≠ null, which would keep the stored one).
        input:
          habit.type === 'binary'
            ? { completed: values.completed, note: values.note.trim() }
            : { quantity, note: values.note.trim() },
      },
      {
        onSuccess: () => {
          toast.success('Registro guardado')
          onOpenChange(false)
        },
        onError: (error) => toast.error(errorMessage(error)),
      },
    )
  })

  const remove = () =>
    logHabit.mutate(
      { habitId: habit.id, date: day.date, input: null },
      {
        onSuccess: () => {
          toast.success('Registro eliminado')
          onOpenChange(false)
        },
        onError: (error) => toast.error(errorMessage(error)),
      },
    )

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={habit.name}
        description={capitalize(formatLongDate(day.date))}
        footer={
          <>
            {hasLog ? (
              <Button
                variant="ghost"
                className="mr-auto text-danger"
                onClick={remove}
                disabled={logHabit.isPending}
              >
                Borrar registro
              </Button>
            ) : null}
            <Button variant="outline" onClick={() => onOpenChange(false)}>
              Cancelar
            </Button>
            <Button type="submit" form="log-form" disabled={logHabit.isPending}>
              {logHabit.isPending ? 'Guardando…' : 'Guardar'}
            </Button>
          </>
        }
      >
        <form id="log-form" onSubmit={onSubmit} noValidate className="space-y-4">
          {habit.type === 'binary' ? (
            <Controller
              control={control}
              name="completed"
              render={({ field }) => (
                <fieldset>
                  <legend className="mb-2 text-sm font-medium">¿Lo cumpliste?</legend>
                  <div className="grid grid-cols-2 gap-2">
                    {[
                      { value: true, label: 'Sí, completado' },
                      { value: false, label: 'No' },
                    ].map((option) => (
                      <label
                        key={String(option.value)}
                        className={cn(
                          'flex cursor-pointer items-center justify-center rounded-lg border px-3 py-2 text-sm font-semibold has-focus-visible:ring-3 has-focus-visible:ring-ring/30',
                          field.value === option.value
                            ? 'border-primary bg-primary-soft text-primary'
                            : 'border-border',
                        )}
                      >
                        <input
                          type="radio"
                          className="sr-only"
                          checked={field.value === option.value}
                          onChange={() => field.onChange(option.value)}
                        />
                        {option.label}
                      </label>
                    ))}
                  </div>
                </fieldset>
              )}
            />
          ) : (
            <FormField
              label={`Cantidad del día (${habit.unit ?? ''})`}
              error={errors.quantity?.message}
              hint={
                day.target
                  ? `Meta de ese día: ${day.target} ${habit.unit ?? ''}. Es el total del día, no se suma.`
                  : 'Es el total del día, no se suma.'
              }
            >
              <Input inputMode="decimal" autoFocus {...register('quantity')} />
            </FormField>
          )}
          <FormField label="Nota (opcional)" error={errors.note?.message} hint="Solo tú puedes verla.">
            <Textarea rows={3} maxLength={500} {...register('note')} />
          </FormField>
        </form>
      </DialogContent>
    </Dialog>
  )
}
