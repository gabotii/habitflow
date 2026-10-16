import { zodResolver } from '@hookform/resolvers/zod'
import { useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormField } from '@/components/form-field'
import { PageHeader } from '@/components/page'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/display'
import { Dialog, DialogContent } from '@/components/ui/dialog'
import { Checkbox, Input, Select } from '@/components/ui/form-controls'
import { useCapabilities } from '@/features/auth/AuthPages'
import { useAuth, useCurrentUser } from '@/features/auth/auth-context'
import { passwordSchema } from '@/features/auth/schemas'
import { api, ApiError, errorMessage } from '@/lib/api-client'
import { formatInstant } from '@/lib/dates'
import { timeZoneOptions } from '@/lib/time-zones'
import type { User } from '@/types/api'

const profileSchema = z.object({
  displayName: z.string().trim().min(1, 'Indica un nombre.').max(80, 'Máximo 80 caracteres.'),
  timeZoneId: z.string().min(1),
  emailRemindersEnabled: z.boolean(),
})

const passwordFormSchema = z
  .object({
    currentPassword: z.string().min(1, 'Introduce tu contraseña actual.'),
    newPassword: passwordSchema,
    confirm: z.string(),
  })
  .refine((v) => v.newPassword === v.confirm, { path: ['confirm'], message: 'Las contraseñas no coinciden.' })
  .refine((v) => v.newPassword !== v.currentPassword, {
    path: ['newPassword'],
    message: 'Debe ser distinta de la actual.',
  })

export function ProfilePage() {
  return (
    <>
      <PageHeader title="Perfil y preferencias" description="Tu cuenta, tu zona horaria y la seguridad." />
      <div className="grid max-w-3xl gap-6">
        <ProfileForm />
        <PasswordForm />
        <DangerZone />
      </div>
    </>
  )
}

function ProfileForm() {
  const user = useCurrentUser()
  const { setUser } = useAuth()
  const queryClient = useQueryClient()
  const capabilities = useCapabilities()
  const zones = useMemo(() => timeZoneOptions(user.timeZoneId), [user.timeZoneId])
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting, isDirty },
    reset,
  } = useForm({
    resolver: zodResolver(profileSchema),
    defaultValues: {
      displayName: user.displayName,
      timeZoneId: user.timeZoneId,
      emailRemindersEnabled: user.emailRemindersEnabled,
    },
  })

  const onSubmit = handleSubmit(async (values) => {
    try {
      const updated = await api.put<User>('/users/me', values)
      setUser(updated)
      reset(values)
      // "Today" may have moved: everything derived from dates is refreshed.
      await queryClient.invalidateQueries()
      toast.success('Perfil actualizado')
    } catch (error) {
      toast.error(errorMessage(error))
    }
  })

  return (
    <Card>
      <CardHeader>
        <CardTitle>Datos personales</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={onSubmit} noValidate className="grid gap-4">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <FormField label="Nombre" error={errors.displayName?.message} required>
              <Input autoComplete="given-name" {...register('displayName')} />
            </FormField>
            <FormField
              label="Correo electrónico"
              hint={`Cuenta creada el ${formatInstant(user.createdAt, user.timeZoneId, { timeStyle: undefined })}.`}
            >
              <Input value={user.email} readOnly />
            </FormField>
          </div>
          <FormField
            label="Zona horaria"
            hint="Define cuándo empieza tu día. Cambiarla no mueve tu historial: los registros guardan su fecha local."
          >
            <Select {...register('timeZoneId')}>
              {zones.map((zone) => (
                <option key={zone} value={zone}>
                  {zone}
                </option>
              ))}
            </Select>
          </FormField>
          <label className="flex items-start gap-3 text-sm">
            <Checkbox
              className="mt-0.5"
              disabled={!capabilities.data?.emailRemindersAvailable}
              {...register('emailRemindersEnabled')}
            />
            <span>
              Recibir por correo los recordatorios configurados con ese canal.
              {!capabilities.data?.emailRemindersAvailable ? (
                <span className="block text-xs text-muted-foreground">
                  El servidor no tiene correo configurado.
                </span>
              ) : null}
            </span>
          </label>
          <div>
            <Button type="submit" disabled={isSubmitting || !isDirty}>
              {isSubmitting ? 'Guardando…' : 'Guardar cambios'}
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  )
}

function PasswordForm() {
  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm({
    resolver: zodResolver(passwordFormSchema),
    defaultValues: { currentPassword: '', newPassword: '', confirm: '' },
  })

  const onSubmit = handleSubmit(async ({ currentPassword, newPassword }) => {
    try {
      await api.post('/users/me/password', { currentPassword, newPassword })
      reset()
      toast.success('Contraseña cambiada. Se ha cerrado la sesión en tus otros dispositivos.')
    } catch (error) {
      if (error instanceof ApiError && error.fieldErrors.currentPassword) {
        setError('currentPassword', { message: error.fieldErrors.currentPassword[0] })
      } else {
        toast.error(errorMessage(error))
      }
    }
  })

  return (
    <Card>
      <CardHeader>
        <CardTitle>Cambiar contraseña</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={onSubmit} noValidate className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <FormField label="Actual" error={errors.currentPassword?.message} required>
            <Input type="password" autoComplete="current-password" {...register('currentPassword')} />
          </FormField>
          <FormField label="Nueva" error={errors.newPassword?.message} required>
            <Input type="password" autoComplete="new-password" {...register('newPassword')} />
          </FormField>
          <FormField label="Repite la nueva" error={errors.confirm?.message} required>
            <Input type="password" autoComplete="new-password" {...register('confirm')} />
          </FormField>
          <div className="sm:col-span-3">
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? 'Cambiando…' : 'Cambiar contraseña'}
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  )
}

function DangerZone() {
  const { clearSession } = useAuth()
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  const confirm = async () => {
    setPending(true)
    setError(null)
    try {
      await api.delete('/users/me', { password })
      clearSession()
      navigate('/login', { replace: true })
      toast.success('Tu cuenta y todos tus datos se han eliminado.')
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setPending(false)
    }
  }

  return (
    <Card className="border-danger/30">
      <CardHeader>
        <CardTitle className="text-danger">Eliminar la cuenta</CardTitle>
      </CardHeader>
      <CardContent>
        <p className="mb-3 text-sm text-muted-foreground">
          Se borrarán definitivamente tu cuenta, hábitos, registros, objetivos, recordatorios y
          notificaciones.
        </p>
        <Button variant="danger" onClick={() => setOpen(true)}>
          Eliminar mi cuenta
        </Button>
      </CardContent>
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent
          title="¿Eliminar tu cuenta para siempre?"
          description="Esta acción no se puede deshacer."
          footer={
            <>
              <Button variant="outline" onClick={() => setOpen(false)}>
                Cancelar
              </Button>
              <Button variant="danger" disabled={pending || !password} onClick={confirm}>
                {pending ? 'Eliminando…' : 'Eliminar definitivamente'}
              </Button>
            </>
          }
        >
          <FormField label="Confirma con tu contraseña" error={error ?? undefined} required>
            <Input
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </FormField>
        </DialogContent>
      </Dialog>
    </Card>
  )
}
