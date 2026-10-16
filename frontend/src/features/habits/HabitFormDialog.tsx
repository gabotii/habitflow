import { zodResolver } from '@hookform/resolvers/zod'
import { Info } from 'lucide-react'
import { useEffect, useMemo } from 'react'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { FormField } from '@/components/form-field'
import { HabitIcon } from '@/components/habit-visuals'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent } from '@/components/ui/dialog'
import { Input, Select, Textarea } from '@/components/ui/form-controls'
import { ApiError, errorMessage } from '@/lib/api-client'
import { browserToday } from '@/lib/dates'
import { describeSchedule, HABIT_COLOR, HABIT_COLORS, HABIT_ICON, HABIT_ICONS, WEEKDAYS } from '@/lib/labels'
import { timeZoneOptions } from '@/lib/time-zones'
import { cn } from '@/lib/utils'
import type { Habit, ScheduleKind } from '@/types/api'
import { useCategories, useCreateHabit, useUpdateHabit } from './api'
import { habitFormSchema, toHabitInput, type HabitFormValues } from './schemas'

const KINDS: { value: ScheduleKind; label: string; hint: string }[] = [
  { value: 'daily', label: 'Todos los días', hint: 'Cada día cuenta para la racha.' },
  { value: 'weekdays', label: 'Días concretos', hint: 'Por ejemplo, lunes, miércoles y viernes.' },
  {
    value: 'timesPerWeek',
    label: 'Veces por semana',
    hint: 'Los días que quieras; la racha cuenta semanas.',
  },
  { value: 'everyNDays', label: 'Cada N días', hint: 'Desde la fecha de inicio, cada N días.' },
]

function defaults(habit: Habit | undefined, categoryId: string): HabitFormValues {
  if (!habit) {
    return {
      name: '',
      description: '',
      categoryId,
      color: 'indigo',
      icon: 'circle-check',
      type: 'binary',
      unit: '',
      targetValue: '',
      kind: 'daily',
      weekdays: ['monday', 'wednesday', 'friday'],
      timesPerWeek: '3',
      intervalDays: '2',
      startDate: browserToday(),
      timeZoneId: '',
    }
  }
  const s = habit.schedule
  return {
    name: habit.name,
    description: habit.description ?? '',
    categoryId: habit.category.id,
    color: habit.color,
    icon: habit.icon,
    type: habit.type,
    unit: habit.unit ?? '',
    targetValue: s.targetValue === null ? '' : String(s.targetValue),
    kind: s.kind,
    weekdays: s.weekdays ?? ['monday', 'wednesday', 'friday'],
    timesPerWeek: String(s.timesPerWeek ?? 3),
    intervalDays: String(s.intervalDays ?? 2),
    startDate: habit.startDate,
    timeZoneId: habit.timeZoneId ?? '',
  }
}

export function HabitFormDialog({
  open,
  onOpenChange,
  habit,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Present when editing. */
  habit?: Habit
}) {
  const navigate = useNavigate()
  const categories = useCategories()
  const create = useCreateHabit()
  const update = useUpdateHabit(habit?.id ?? '')
  const zones = useMemo(() => timeZoneOptions(habit?.timeZoneId), [habit?.timeZoneId])
  const firstCategory = categories.data?.[0]?.id ?? ''

  const {
    register,
    control,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<HabitFormValues>({
    resolver: zodResolver(habitFormSchema),
    defaultValues: defaults(habit, firstCategory),
  })

  useEffect(() => {
    if (open) reset(defaults(habit, firstCategory))
  }, [open, habit, firstCategory, reset])

  const values = useWatch({ control })
  const editing = Boolean(habit)

  const onSubmit = handleSubmit(async (form) => {
    const input = toHabitInput(form)
    try {
      if (habit) {
        await update.mutateAsync({ ...input, version: habit.version })
        toast.success('Hábito actualizado')
        onOpenChange(false)
      } else {
        const created = await create.mutateAsync(input)
        toast.success('Hábito creado. ¡A por el primer día!')
        onOpenChange(false)
        navigate(`/habitos/${created.id}`)
      }
    } catch (error) {
      if (error instanceof ApiError) {
        for (const [field, messages] of Object.entries(error.fieldErrors)) {
          const name = field.replace(/^schedule\./, '') as keyof HabitFormValues
          if (name in form) setError(name, { message: messages[0] })
        }
      }
      toast.error(errorMessage(error))
    }
  })

  const preview = describeSchedule(
    {
      kind: values.kind ?? 'daily',
      weekdays: (values.weekdays ?? []).filter((d) => d !== undefined),
      timesPerWeek: Number(values.timesPerWeek),
      intervalDays: Number(values.intervalDays),
      targetValue:
        values.type === 'quantity' && values.targetValue
          ? Number(values.targetValue.replace(',', '.'))
          : null,
    },
    values.unit,
  )

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        size="lg"
        title={editing ? 'Editar hábito' : 'Nuevo hábito'}
        description={editing ? undefined : 'Empieza pequeño: un hábito fácil de cumplir dura más.'}
        footer={
          <>
            <Button variant="outline" onClick={() => onOpenChange(false)}>
              Cancelar
            </Button>
            <Button type="submit" form="habit-form" disabled={isSubmitting}>
              {isSubmitting ? 'Guardando…' : editing ? 'Guardar cambios' : 'Crear hábito'}
            </Button>
          </>
        }
      >
        <form id="habit-form" onSubmit={onSubmit} noValidate className="grid gap-5">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-[1fr_auto]">
            <FormField label="Nombre" error={errors.name?.message} required>
              <Input placeholder="Leer 20 minutos" autoFocus {...register('name')} />
            </FormField>
            <div className="hidden items-end sm:flex">
              <HabitIcon icon={values.icon ?? 'circle-check'} color={values.color ?? 'indigo'} size="lg" />
            </div>
          </div>

          <FormField label="Descripción" error={errors.description?.message}>
            <Textarea
              rows={2}
              placeholder="Opcional: por qué te importa o cómo lo harás."
              {...register('description')}
            />
          </FormField>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <FormField label="Categoría" error={errors.categoryId?.message} required>
              <Select {...register('categoryId')} disabled={categories.isLoading}>
                {categories.data?.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </Select>
            </FormField>
            <FormField
              label="Tipo"
              hint={
                editing ? 'El tipo no se puede cambiar: cambiaría el significado del historial.' : undefined
              }
            >
              <Select {...register('type')} disabled={editing}>
                <option value="binary">Sí / no (lo hice o no)</option>
                <option value="quantity">Cantidad (minutos, páginas, litros…)</option>
              </Select>
            </FormField>
          </div>

          {values.type === 'quantity' ? (
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <FormField label="Unidad" error={errors.unit?.message} required>
                <Input placeholder="páginas" {...register('unit')} />
              </FormField>
              <FormField
                label="Meta diaria"
                error={errors.targetValue?.message}
                hint="Opcional. Sin meta, cualquier cantidad cuenta como cumplido."
              >
                <Input inputMode="decimal" placeholder="20" {...register('targetValue')} />
              </FormField>
            </div>
          ) : null}

          <Controller
            control={control}
            name="kind"
            render={({ field }) => (
              <fieldset>
                <legend className="mb-2 text-sm font-medium">Frecuencia</legend>
                <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                  {KINDS.map((kind) => (
                    <label
                      key={kind.value}
                      className={cn(
                        'cursor-pointer rounded-lg border p-3 text-sm has-focus-visible:ring-3 has-focus-visible:ring-ring/30',
                        field.value === kind.value
                          ? 'border-primary bg-primary-soft'
                          : 'border-border hover:bg-muted',
                      )}
                    >
                      <input
                        type="radio"
                        name="kind"
                        className="sr-only"
                        value={kind.value}
                        checked={field.value === kind.value}
                        onChange={() => field.onChange(kind.value)}
                      />
                      <span className="block font-semibold">{kind.label}</span>
                      <span className="text-xs text-muted-foreground">{kind.hint}</span>
                    </label>
                  ))}
                </div>
              </fieldset>
            )}
          />

          {values.kind === 'weekdays' ? (
            <Controller
              control={control}
              name="weekdays"
              render={({ field }) => (
                <fieldset>
                  <legend className="mb-2 text-sm font-medium">Días</legend>
                  <div className="flex flex-wrap gap-2">
                    {WEEKDAYS.map((day) => {
                      const checked = field.value.includes(day.value)
                      return (
                        <label
                          key={day.value}
                          title={day.label}
                          className={cn(
                            'grid size-10 cursor-pointer place-items-center rounded-full border text-sm font-bold has-focus-visible:ring-3 has-focus-visible:ring-ring/30',
                            checked
                              ? 'border-primary bg-primary text-primary-foreground'
                              : 'border-border hover:bg-muted',
                          )}
                        >
                          <input
                            type="checkbox"
                            className="sr-only"
                            aria-label={day.label}
                            checked={checked}
                            onChange={() =>
                              field.onChange(
                                checked
                                  ? field.value.filter((d) => d !== day.value)
                                  : [...field.value, day.value],
                              )
                            }
                          />
                          {day.short}
                        </label>
                      )
                    })}
                  </div>
                  {errors.weekdays ? (
                    <p role="alert" className="mt-1 text-xs font-medium text-danger">
                      {errors.weekdays.message}
                    </p>
                  ) : null}
                </fieldset>
              )}
            />
          ) : null}
          {values.kind === 'timesPerWeek' ? (
            <FormField
              label="Veces por semana"
              error={errors.timesPerWeek?.message}
              hint="La semana va de lunes a domingo. Si empiezas a mitad de semana, la meta se reparte proporcionalmente."
              className="max-w-xs"
            >
              <Input type="number" min={1} max={7} {...register('timesPerWeek')} />
            </FormField>
          ) : null}
          {values.kind === 'everyNDays' ? (
            <FormField label="Cada cuántos días" error={errors.intervalDays?.message} className="max-w-xs">
              <Input type="number" min={2} max={365} {...register('intervalDays')} />
            </FormField>
          ) : null}

          <p className="flex items-start gap-2 rounded-lg bg-muted px-3 py-2 text-sm">
            <Info className="mt-0.5 size-4 shrink-0 text-primary" aria-hidden />
            <span>
              <strong>{preview}</strong>
              {editing
                ? '. Si cambias la frecuencia o la meta, se aplica desde hoy: los días anteriores conservan sus reglas.'
                : null}
            </span>
          </p>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <FormField
              label="Fecha de inicio"
              error={errors.startDate?.message}
              hint={
                habit?.hasLogs
                  ? 'No se puede cambiar porque el hábito ya tiene registros.'
                  : 'Puede ser pasada para registrar días anteriores.'
              }
            >
              <Input type="date" disabled={habit?.hasLogs} {...register('startDate')} />
            </FormField>
            <FormField
              label="Zona horaria del hábito"
              hint="Normalmente, la de tu perfil. Cambia cuándo empieza «hoy»."
            >
              <Select {...register('timeZoneId')}>
                <option value="">Usar la de mi perfil</option>
                {zones.map((zone) => (
                  <option key={zone} value={zone}>
                    {zone}
                  </option>
                ))}
              </Select>
            </FormField>
          </div>

          <Controller
            control={control}
            name="color"
            render={({ field }) => (
              <fieldset>
                <legend className="mb-2 text-sm font-medium">Color</legend>
                <div className="flex flex-wrap gap-2">
                  {HABIT_COLORS.map((color) => (
                    <label
                      key={color}
                      title={HABIT_COLOR[color].label}
                      className={cn(
                        'size-8 cursor-pointer rounded-full ring-offset-2 has-focus-visible:ring-3',
                        HABIT_COLOR[color].solid,
                        field.value === color ? cn('ring-2', HABIT_COLOR[color].ring) : '',
                      )}
                    >
                      <input
                        type="radio"
                        className="sr-only"
                        aria-label={HABIT_COLOR[color].label}
                        checked={field.value === color}
                        onChange={() => field.onChange(color)}
                      />
                    </label>
                  ))}
                </div>
              </fieldset>
            )}
          />

          <Controller
            control={control}
            name="icon"
            render={({ field }) => (
              <fieldset>
                <legend className="mb-2 text-sm font-medium">Icono</legend>
                <div className="grid grid-cols-6 gap-2 sm:grid-cols-10">
                  {HABIT_ICONS.map((key) => {
                    const Icon = HABIT_ICON[key].icon
                    return (
                      <label
                        key={key}
                        title={HABIT_ICON[key].label}
                        className={cn(
                          'grid aspect-square cursor-pointer place-items-center rounded-lg border has-focus-visible:ring-3 has-focus-visible:ring-ring/30',
                          field.value === key
                            ? 'border-primary bg-primary-soft text-primary'
                            : 'border-border hover:bg-muted',
                        )}
                      >
                        <input
                          type="radio"
                          className="sr-only"
                          aria-label={HABIT_ICON[key].label}
                          checked={field.value === key}
                          onChange={() => field.onChange(key)}
                        />
                        <Icon className="size-5" aria-hidden />
                      </label>
                    )
                  })}
                </div>
              </fieldset>
            )}
          />
        </form>
      </DialogContent>
    </Dialog>
  )
}
