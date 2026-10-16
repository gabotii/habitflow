import { zodResolver } from '@hookform/resolvers/zod'
import { BellOff, BellRing, Mail, Pencil, Plus, Trash2 } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormField } from '@/components/form-field'
import { EmptyState, ErrorState, ListSkeleton, PageHeader } from '@/components/page'
import { Button } from '@/components/ui/button'
import { Badge, Card } from '@/components/ui/display'
import { ConfirmDialog, Dialog, DialogContent } from '@/components/ui/dialog'
import { Input, Select, Textarea } from '@/components/ui/form-controls'
import { useCapabilities } from '@/features/auth/AuthPages'
import { useCurrentUser } from '@/features/auth/auth-context'
import { useGoals } from '@/features/goals/api'
import { useHabits } from '@/features/habits/api'
import { errorMessage } from '@/lib/api-client'
import { formatInstant } from '@/lib/dates'
import { WEEKDAYS } from '@/lib/labels'
import { timeZoneOptions } from '@/lib/time-zones'
import { cn } from '@/lib/utils'
import type { Reminder, ReminderChannel, Weekday } from '@/types/api'
import { useDeleteReminder, useReminders, useSaveReminder, useToggleReminder } from './api'

function describeDays(days: Weekday[]): string {
  if (days.length === 7) return 'Todos los días'
  if (days.length === 5 && !days.includes('saturday') && !days.includes('sunday')) return 'De lunes a viernes'
  return WEEKDAYS.filter((d) => days.includes(d.value))
    .map((d) => d.label.slice(0, 3))
    .join(', ')
}

export function RemindersPage() {
  const reminders = useReminders()
  const toggle = useToggleReminder()
  const remove = useDeleteReminder()
  const capabilities = useCapabilities()
  const [editing, setEditing] = useState<Reminder | 'new' | null>(null)
  const [deleting, setDeleting] = useState<Reminder | null>(null)

  return (
    <>
      <PageHeader
        title="Recordatorios"
        description="Avisos recurrentes en tu zona horaria. Se muestran dentro de la app (campana) y, si lo activas, por correo."
        actions={
          <Button onClick={() => setEditing('new')}>
            <Plus /> Nuevo recordatorio
          </Button>
        }
      />
      <p className="mb-5 rounded-lg bg-muted px-3 py-2 text-sm text-muted-foreground">
        Canales disponibles: <strong className="text-foreground">notificación en la app</strong>
        {capabilities.data?.emailRemindersAvailable ? (
          <>
            {' '}
            y <strong className="text-foreground">correo electrónico</strong> (actívalo en tu perfil)
          </>
        ) : (
          ' (el correo no está configurado en este servidor)'
        )}
        . Las notificaciones push no están disponibles.
      </p>

      {reminders.isPending ? (
        <ListSkeleton rows={3} />
      ) : reminders.isError ? (
        <ErrorState error={reminders.error} onRetry={() => reminders.refetch()} />
      ) : reminders.data.length === 0 ? (
        <EmptyState
          icon={BellRing}
          title="Sin recordatorios"
          description="Programa un aviso a la hora en que mejor encaje cada hábito. Respeta los cambios de horario de verano."
          action={
            <Button onClick={() => setEditing('new')}>
              <Plus /> Crear recordatorio
            </Button>
          }
        />
      ) : (
        <Card className="divide-y divide-border">
          {reminders.data.map((r) => (
            <div key={r.id} className="flex flex-col gap-3 p-4 sm:flex-row sm:items-center">
              <div className="flex flex-1 items-start gap-3">
                <span
                  className={cn(
                    'grid size-12 shrink-0 place-items-center rounded-xl text-sm font-bold',
                    r.isActive ? 'bg-primary-soft text-primary' : 'bg-muted text-muted-foreground',
                  )}
                >
                  {r.localTime}
                </span>
                <div className="min-w-0">
                  <p className="font-semibold">{r.title}</p>
                  <p className="text-sm text-muted-foreground">
                    {describeDays(r.weekdays)} · {r.timeZoneId}
                    {r.habit ? ` · ${r.habit.name}` : r.goal ? ` · ${r.goal.name}` : ''}
                  </p>
                  <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                    {r.channel === 'inAppAndEmail' ? (
                      <Badge className="bg-muted text-muted-foreground">
                        <Mail aria-hidden /> También por correo
                      </Badge>
                    ) : null}
                    {r.isActive && r.nextFireAt ? (
                      <span>Próximo: {formatInstant(r.nextFireAt, r.timeZoneId)}</span>
                    ) : (
                      <span className="inline-flex items-center gap-1">
                        <BellOff className="size-3.5" aria-hidden /> Desactivado
                      </span>
                    )}
                  </div>
                </div>
              </div>
              <div className="flex items-center gap-2 self-end sm:self-auto">
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    role="switch"
                    className="size-4 accent-primary"
                    checked={r.isActive}
                    aria-label={r.isActive ? `Desactivar ${r.title}` : `Activar ${r.title}`}
                    onChange={() =>
                      toggle.mutate(
                        { id: r.id, isActive: !r.isActive },
                        { onError: (e) => toast.error(errorMessage(e)) },
                      )
                    }
                  />
                  Activo
                </label>
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label={`Editar ${r.title}`}
                  onClick={() => setEditing(r)}
                >
                  <Pencil />
                </Button>
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label={`Eliminar ${r.title}`}
                  onClick={() => setDeleting(r)}
                >
                  <Trash2 />
                </Button>
              </div>
            </div>
          ))}
        </Card>
      )}

      {editing ? (
        <ReminderFormDialog
          reminder={editing === 'new' ? undefined : editing}
          onClose={() => setEditing(null)}
        />
      ) : null}
      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title="¿Eliminar el recordatorio?"
        description={`«${deleting?.title}» dejará de avisarte. Las notificaciones ya enviadas se conservan.`}
        confirmLabel="Eliminar"
        pending={remove.isPending}
        onConfirm={() =>
          deleting &&
          remove.mutate(deleting.id, {
            onSuccess: () => {
              toast.success('Recordatorio eliminado')
              setDeleting(null)
            },
            onError: (e) => toast.error(errorMessage(e)),
          })
        }
      />
    </>
  )
}

const schema = z.object({
  title: z.string().trim().min(1, 'Ponle un título.').max(100, 'Máximo 100 caracteres.'),
  description: z.string().max(300, 'Máximo 300 caracteres.'),
  localTime: z.string().regex(/^([01]\d|2[0-3]):[0-5]\d$/, 'Hora no válida.'),
  weekdays: z.array(z.custom<Weekday>()).min(1, 'Elige al menos un día.'),
  timeZoneId: z.string().min(1),
  channel: z.enum(['inApp', 'inAppAndEmail'] satisfies ReminderChannel[]),
  target: z.string(),
})

type FormValues = z.infer<typeof schema>

function ReminderFormDialog({ reminder, onClose }: { reminder?: Reminder; onClose: () => void }) {
  const user = useCurrentUser()
  const save = useSaveReminder(reminder?.id)
  const habits = useHabits({ status: 'active', page: 1, pageSize: 100 })
  const goals = useGoals('open')
  const zones = useMemo(
    () => timeZoneOptions(reminder?.timeZoneId ?? user.timeZoneId),
    [reminder, user.timeZoneId],
  )
  const {
    register,
    control,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema) })

  useEffect(() => {
    reset({
      title: reminder?.title ?? '',
      description: reminder?.description ?? '',
      localTime: reminder?.localTime ?? '08:00',
      weekdays: reminder?.weekdays ?? WEEKDAYS.map((d) => d.value),
      timeZoneId: reminder?.timeZoneId ?? user.timeZoneId,
      channel: reminder?.channel ?? 'inApp',
      target: reminder?.habit
        ? `habit:${reminder.habit.id}`
        : reminder?.goal
          ? `goal:${reminder.goal.id}`
          : '',
    })
  }, [reminder, user.timeZoneId, reset])

  const onSubmit = handleSubmit(async (v) => {
    const [kind, id] = v.target.split(':')
    try {
      await save.mutateAsync({
        title: v.title.trim(),
        description: v.description.trim() || null,
        localTime: v.localTime,
        weekdays: v.weekdays,
        timeZoneId: v.timeZoneId,
        channel: v.channel,
        habitId: kind === 'habit' ? id : null,
        goalId: kind === 'goal' ? id : null,
      })
      toast.success(reminder ? 'Recordatorio actualizado' : 'Recordatorio creado')
      onClose()
    } catch (error) {
      toast.error(errorMessage(error))
    }
  })

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={reminder ? 'Editar recordatorio' : 'Nuevo recordatorio'}
        footer={
          <>
            <Button variant="outline" onClick={onClose}>
              Cancelar
            </Button>
            <Button type="submit" form="reminder-form" disabled={isSubmitting}>
              {isSubmitting ? 'Guardando…' : 'Guardar'}
            </Button>
          </>
        }
      >
        <form id="reminder-form" onSubmit={onSubmit} noValidate className="grid gap-4">
          <FormField label="Título" error={errors.title?.message} required>
            <Input placeholder="Hora de leer" autoFocus {...register('title')} />
          </FormField>
          <FormField label="Descripción" error={errors.description?.message}>
            <Textarea rows={2} {...register('description')} />
          </FormField>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <FormField label="Hora" error={errors.localTime?.message} required>
              <Input type="time" step={60} {...register('localTime')} />
            </FormField>
            <FormField
              label="Zona horaria"
              hint="Si la hora no existe por el cambio de horario, se avisa justo después."
            >
              <Select {...register('timeZoneId')}>
                {zones.map((z) => (
                  <option key={z} value={z}>
                    {z}
                  </option>
                ))}
              </Select>
            </FormField>
          </div>
          <Controller
            control={control}
            name="weekdays"
            render={({ field }) => (
              <fieldset>
                <legend className="mb-2 text-sm font-medium">Días</legend>
                <div className="flex flex-wrap gap-2">
                  {WEEKDAYS.map((day) => {
                    const checked = field.value?.includes(day.value) ?? false
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
                                : [...(field.value ?? []), day.value],
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
          <FormField label="Vinculado a" hint="Opcional: la notificación abrirá ese hábito u objetivo.">
            <Select {...register('target')}>
              <option value="">Nada</option>
              <optgroup label="Hábitos">
                {habits.data?.items.map((h) => (
                  <option key={h.id} value={`habit:${h.id}`}>
                    {h.name}
                  </option>
                ))}
              </optgroup>
              <optgroup label="Objetivos">
                {goals.data?.map((g) => (
                  <option key={g.id} value={`goal:${g.id}`}>
                    {g.name}
                  </option>
                ))}
              </optgroup>
            </Select>
          </FormField>
          <FormField
            label="Canal"
            hint="El correo solo se envía si el servidor lo tiene configurado y lo has permitido en tu perfil."
          >
            <Select {...register('channel')}>
              <option value="inApp">Solo en la app</option>
              <option value="inAppAndEmail">En la app y por correo</option>
            </Select>
          </FormField>
        </form>
      </DialogContent>
    </Dialog>
  )
}
