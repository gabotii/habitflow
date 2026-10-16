import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect } from 'react'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormField } from '@/components/form-field'
import { HabitIcon } from '@/components/habit-visuals'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent } from '@/components/ui/dialog'
import { Input, Textarea } from '@/components/ui/form-controls'
import { useHabits } from '@/features/habits/api'
import { errorMessage } from '@/lib/api-client'
import { browserToday } from '@/lib/dates'
import { GOAL_METRIC } from '@/lib/labels'
import { cn } from '@/lib/utils'
import type { Goal, GoalMetric } from '@/types/api'
import { useCreateGoal, useUpdateGoal } from './api'

const schema = z
  .object({
    name: z.string().trim().min(1, 'Ponle un nombre.').max(100, 'Máximo 100 caracteres.'),
    description: z.string().max(1000, 'Máximo 1000 caracteres.'),
    metric: z.enum(['manual', 'habitCompletions', 'habitQuantity', 'habitStreak'] satisfies GoalMetric[]),
    targetValue: z
      .string()
      .trim()
      .refine(
        (v) => /^\d+([.,]\d{1,2})?$/.test(v) && Number(v.replace(',', '.')) > 0,
        'Indica una meta mayor que 0.',
      ),
    unit: z.string().trim().max(24, 'Máximo 24 caracteres.'),
    startDate: z.string(),
    deadline: z.string(),
    habitIds: z.array(z.string()),
  })
  .superRefine((v, ctx) => {
    if (v.metric === 'manual' && !v.unit)
      ctx.addIssue({ code: 'custom', path: ['unit'], message: 'Indica la unidad.' })
    if (v.metric !== 'manual' && v.habitIds.length === 0)
      ctx.addIssue({ code: 'custom', path: ['habitIds'], message: 'Elige al menos un hábito.' })
    if (v.metric === 'habitStreak' && v.habitIds.length > 1)
      ctx.addIssue({
        code: 'custom',
        path: ['habitIds'],
        message: 'Una racha se mide sobre un único hábito.',
      })
    if (
      (v.metric === 'habitCompletions' || v.metric === 'habitStreak') &&
      !/^\d+$/.test(v.targetValue.trim())
    )
      ctx.addIssue({ code: 'custom', path: ['targetValue'], message: 'Usa un número entero.' })
    if (v.deadline && v.startDate && v.deadline < v.startDate)
      ctx.addIssue({ code: 'custom', path: ['deadline'], message: 'No puede ser anterior al inicio.' })
  })

type FormValues = z.infer<typeof schema>

function defaults(goal?: Goal): FormValues {
  return {
    name: goal?.name ?? '',
    description: goal?.description ?? '',
    metric: goal?.metric ?? 'manual',
    targetValue: goal ? String(goal.targetValue) : '',
    unit: goal?.unit ?? '',
    startDate: goal?.startDate ?? browserToday(),
    deadline: goal?.deadline ?? '',
    habitIds: goal?.habits.map((h) => h.id) ?? [],
  }
}

export function GoalFormDialog({
  open,
  onOpenChange,
  goal,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  goal?: Goal
}) {
  const navigate = useNavigate()
  const create = useCreateGoal()
  const update = useUpdateGoal(goal?.id ?? '')
  const habits = useHabits({ status: 'active', page: 1, pageSize: 100 })
  const editing = Boolean(goal)
  const {
    register,
    control,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: defaults(goal) })

  useEffect(() => {
    if (open) reset(defaults(goal))
  }, [open, goal, reset])

  const metric = useWatch({ control, name: 'metric' })
  const selectedHabits = useWatch({ control, name: 'habitIds' })
  const eligible = (habits.data?.items ?? []).filter(
    (h) => metric !== 'habitQuantity' || h.type === 'quantity',
  )
  const quantityUnit =
    metric === 'habitQuantity' ? (eligible.find((h) => selectedHabits.includes(h.id))?.unit ?? null) : null

  const onSubmit = handleSubmit(async (v) => {
    const target = Number(v.targetValue.replace(',', '.'))
    try {
      if (goal) {
        await update.mutateAsync({
          name: v.name.trim(),
          description: v.description.trim() || null,
          targetValue: target,
          unit: goal.metric === 'manual' ? v.unit.trim() : null,
          deadline: v.deadline || null,
          version: goal.version,
        })
        toast.success('Objetivo actualizado')
        onOpenChange(false)
      } else {
        const created = await create.mutateAsync({
          name: v.name.trim(),
          description: v.description.trim() || null,
          metric: v.metric,
          targetValue: target,
          unit: v.metric === 'manual' ? v.unit.trim() : null,
          startDate: v.startDate || null,
          deadline: v.deadline || null,
          habitIds: v.metric === 'manual' ? [] : v.habitIds,
        })
        toast.success('Objetivo creado')
        onOpenChange(false)
        navigate(`/objetivos/${created.id}`)
      }
    } catch (error) {
      toast.error(errorMessage(error))
    }
  })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        size="lg"
        title={editing ? 'Editar objetivo' : 'Nuevo objetivo'}
        description={
          editing
            ? 'El tipo de métrica y los hábitos vinculados no se pueden cambiar: definen qué significa el progreso.'
            : 'Una meta concreta y medible, con fecha límite si la tiene.'
        }
        footer={
          <>
            <Button variant="outline" onClick={() => onOpenChange(false)}>
              Cancelar
            </Button>
            <Button type="submit" form="goal-form" disabled={isSubmitting}>
              {isSubmitting ? 'Guardando…' : editing ? 'Guardar cambios' : 'Crear objetivo'}
            </Button>
          </>
        }
      >
        <form id="goal-form" onSubmit={onSubmit} noValidate className="grid gap-4">
          <FormField label="Nombre" error={errors.name?.message} required>
            <Input placeholder="Leer 12 libros este año" autoFocus {...register('name')} />
          </FormField>
          <FormField label="Descripción" error={errors.description?.message}>
            <Textarea rows={2} {...register('description')} />
          </FormField>

          {!editing ? (
            <Controller
              control={control}
              name="metric"
              render={({ field }) => (
                <fieldset>
                  <legend className="mb-2 text-sm font-medium">¿Cómo se mide?</legend>
                  <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                    {(Object.keys(GOAL_METRIC) as GoalMetric[]).map((m) => (
                      <label
                        key={m}
                        className={cn(
                          'cursor-pointer rounded-lg border p-3 text-sm has-focus-visible:ring-3 has-focus-visible:ring-ring/30',
                          field.value === m
                            ? 'border-primary bg-primary-soft'
                            : 'border-border hover:bg-muted',
                        )}
                      >
                        <input
                          type="radio"
                          className="sr-only"
                          checked={field.value === m}
                          onChange={() => field.onChange(m)}
                        />
                        <span className="block font-semibold">{GOAL_METRIC[m].label}</span>
                        <span className="text-xs text-muted-foreground">{GOAL_METRIC[m].description}</span>
                      </label>
                    ))}
                  </div>
                </fieldset>
              )}
            />
          ) : null}

          {!editing && metric !== 'manual' ? (
            <Controller
              control={control}
              name="habitIds"
              render={({ field }) => (
                <fieldset>
                  <legend className="mb-2 text-sm font-medium">
                    {metric === 'habitStreak' ? 'Hábito' : 'Hábitos vinculados'}
                  </legend>
                  {eligible.length === 0 ? (
                    <p className="text-sm text-muted-foreground">
                      {metric === 'habitQuantity'
                        ? 'No tienes hábitos cuantificables activos.'
                        : 'No tienes hábitos activos. Crea uno primero.'}
                    </p>
                  ) : (
                    <ul className="max-h-48 space-y-1 overflow-y-auto rounded-lg border border-border p-2">
                      {eligible.map((habit) => {
                        const checked = field.value.includes(habit.id)
                        return (
                          <li key={habit.id}>
                            <label className="flex cursor-pointer items-center gap-3 rounded-md p-1.5 hover:bg-muted">
                              <input
                                type={metric === 'habitStreak' ? 'radio' : 'checkbox'}
                                name="goal-habits"
                                className="accent-primary"
                                checked={checked}
                                onChange={() =>
                                  field.onChange(
                                    metric === 'habitStreak'
                                      ? [habit.id]
                                      : checked
                                        ? field.value.filter((id) => id !== habit.id)
                                        : [...field.value, habit.id],
                                  )
                                }
                              />
                              <HabitIcon icon={habit.icon} color={habit.color} size="sm" />
                              <span className="text-sm">
                                {habit.name}
                                {habit.unit ? (
                                  <span className="text-muted-foreground"> · {habit.unit}</span>
                                ) : null}
                              </span>
                            </label>
                          </li>
                        )
                      })}
                    </ul>
                  )}
                  {errors.habitIds ? (
                    <p role="alert" className="mt-1 text-xs font-medium text-danger">
                      {errors.habitIds.message}
                    </p>
                  ) : null}
                </fieldset>
              )}
            />
          ) : null}

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <FormField label="Meta" error={errors.targetValue?.message} required>
              <Input inputMode="decimal" placeholder="12" {...register('targetValue')} />
            </FormField>
            {metric === 'manual' ? (
              <FormField label="Unidad" error={errors.unit?.message} required>
                <Input placeholder="libros" {...register('unit')} />
              </FormField>
            ) : (
              <FormField label="Unidad">
                <Input
                  readOnly
                  value={
                    editing
                      ? goal!.unit
                      : metric === 'habitCompletions'
                        ? 'veces'
                        : metric === 'habitStreak'
                          ? 'días o semanas (según el hábito)'
                          : (quantityUnit ?? 'la del hábito')
                  }
                />
              </FormField>
            )}
          </div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <FormField
              label="Inicio"
              hint={
                editing
                  ? 'No se puede cambiar.'
                  : metric === 'manual'
                    ? undefined
                    : 'Solo cuentan los días desde esta fecha.'
              }
            >
              <Input type="date" disabled={editing} {...register('startDate')} />
            </FormField>
            <FormField label="Fecha límite" error={errors.deadline?.message} hint="Opcional.">
              <Input type="date" {...register('deadline')} />
            </FormField>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  )
}
