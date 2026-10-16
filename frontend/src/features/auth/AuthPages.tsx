import { zodResolver } from '@hookform/resolvers/zod'
import { useQuery } from '@tanstack/react-query'
import { CheckCircle2 } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router'
import { z } from 'zod'
import { FormField } from '@/components/form-field'
import { Button } from '@/components/ui/button'
import { Checkbox, Input, Select } from '@/components/ui/form-controls'
import { api, errorMessage } from '@/lib/api-client'
import { browserTimeZone, timeZoneOptions } from '@/lib/time-zones'
import type { AuthCapabilities } from '@/types/api'
import { AuthLayout } from './AuthLayout'
import { useAuth } from './auth-context'
import { passwordSchema } from './schemas'

function ServerError({ message }: { message: string | null }) {
  return message ? (
    <p role="alert" className="rounded-lg bg-danger-soft px-3 py-2 text-sm text-danger">
      {message}
    </p>
  ) : null
}

export function useCapabilities() {
  return useQuery({
    queryKey: ['auth', 'capabilities'],
    queryFn: () => api.get<AuthCapabilities>('/auth/capabilities'),
    staleTime: Infinity,
  })
}

const loginSchema = z.object({
  email: z.email('Introduce un correo electrónico válido.'),
  password: z.string().min(1, 'Introduce tu contraseña.'),
  rememberMe: z.boolean(),
})

export function LoginPage() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const capabilities = useCapabilities()
  const [serverError, setServerError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: '', password: '', rememberMe: true },
  })

  const from = (location.state as { from?: string } | null)?.from ?? '/'

  const onSubmit = handleSubmit(async (values) => {
    setServerError(null)
    try {
      await login(values.email, values.password, values.rememberMe)
      navigate(from, { replace: true })
    } catch (error) {
      setServerError(errorMessage(error))
    }
  })

  return (
    <AuthLayout title="Inicia sesión" subtitle="Bienvenido de nuevo. Tus rachas te esperan.">
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        <ServerError message={serverError} />
        <FormField label="Correo electrónico" error={errors.email?.message} required>
          <Input type="email" autoComplete="email" {...register('email')} />
        </FormField>
        <FormField label="Contraseña" error={errors.password?.message} required>
          <Input type="password" autoComplete="current-password" {...register('password')} />
        </FormField>
        <div className="flex items-center justify-between gap-2">
          <label className="flex items-center gap-2 text-sm">
            <Checkbox {...register('rememberMe')} />
            Mantener la sesión iniciada
          </label>
          {capabilities.data?.passwordResetAvailable ? (
            <Link to="/recuperar-contrasena" className="text-sm font-semibold text-primary hover:underline">
              ¿La olvidaste?
            </Link>
          ) : null}
        </div>
        <Button type="submit" className="w-full" disabled={isSubmitting}>
          {isSubmitting ? 'Entrando…' : 'Entrar'}
        </Button>
      </form>
      <p className="mt-6 text-center text-sm text-muted-foreground">
        ¿No tienes cuenta?{' '}
        <Link to="/registro" className="font-semibold text-primary hover:underline">
          Crea una gratis
        </Link>
      </p>
    </AuthLayout>
  )
}

const registerSchema = z
  .object({
    displayName: z
      .string()
      .trim()
      .min(1, 'Indica cómo quieres que te llamemos.')
      .max(80, 'Máximo 80 caracteres.'),
    email: z.email('Introduce un correo electrónico válido.'),
    password: passwordSchema,
    confirmPassword: z.string(),
    timeZoneId: z.string().min(1, 'Elige tu zona horaria.'),
  })
  .refine((v) => v.password === v.confirmPassword, {
    path: ['confirmPassword'],
    message: 'Las contraseñas no coinciden.',
  })

export function RegisterPage() {
  const { register: signUp } = useAuth()
  const navigate = useNavigate()
  const zones = useMemo(() => timeZoneOptions(), [])
  const [serverError, setServerError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm({
    resolver: zodResolver(registerSchema),
    defaultValues: {
      displayName: '',
      email: '',
      password: '',
      confirmPassword: '',
      timeZoneId: browserTimeZone(),
    },
  })

  const onSubmit = handleSubmit(async ({ confirmPassword: _, ...values }) => {
    setServerError(null)
    try {
      await signUp(values)
      navigate('/', { replace: true })
    } catch (error) {
      setServerError(errorMessage(error))
    }
  })

  return (
    <AuthLayout title="Crea tu cuenta" subtitle="Empieza con un hábito. Lo demás vendrá.">
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        <ServerError message={serverError} />
        <FormField label="Nombre" error={errors.displayName?.message} required>
          <Input autoComplete="given-name" {...register('displayName')} />
        </FormField>
        <FormField label="Correo electrónico" error={errors.email?.message} required>
          <Input type="email" autoComplete="email" {...register('email')} />
        </FormField>
        <FormField
          label="Contraseña"
          error={errors.password?.message}
          hint="Mínimo 8 caracteres, con mayúscula, minúscula y número."
          required
        >
          <Input type="password" autoComplete="new-password" {...register('password')} />
        </FormField>
        <FormField label="Repite la contraseña" error={errors.confirmPassword?.message} required>
          <Input type="password" autoComplete="new-password" {...register('confirmPassword')} />
        </FormField>
        <FormField
          label="Zona horaria"
          error={errors.timeZoneId?.message}
          hint="Define cuándo empieza y termina tu día para las rachas."
          required
        >
          <Select {...register('timeZoneId')}>
            {zones.map((zone) => (
              <option key={zone} value={zone}>
                {zone}
              </option>
            ))}
          </Select>
        </FormField>
        <Button type="submit" className="w-full" disabled={isSubmitting}>
          {isSubmitting ? 'Creando cuenta…' : 'Crear cuenta'}
        </Button>
      </form>
      <p className="mt-6 text-center text-sm text-muted-foreground">
        ¿Ya tienes cuenta?{' '}
        <Link to="/login" className="font-semibold text-primary hover:underline">
          Inicia sesión
        </Link>
      </p>
    </AuthLayout>
  )
}

const forgotSchema = z.object({ email: z.email('Introduce un correo electrónico válido.') })

export function ForgotPasswordPage() {
  const capabilities = useCapabilities()
  const [sent, setSent] = useState(false)
  const [serverError, setServerError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm({ resolver: zodResolver(forgotSchema), defaultValues: { email: '' } })

  const onSubmit = handleSubmit(async (values) => {
    setServerError(null)
    try {
      await api.post('/auth/forgot-password', values, true)
      setSent(true)
    } catch (error) {
      setServerError(errorMessage(error))
    }
  })

  if (capabilities.data && !capabilities.data.passwordResetAvailable) {
    return (
      <AuthLayout title="Recuperar contraseña" subtitle="Esta función no está disponible en este servidor.">
        <p className="text-sm text-muted-foreground">
          El servidor no tiene correo configurado, así que no puede enviar enlaces de recuperación. Contacta
          con la persona que administra esta instalación.
        </p>
        <Link to="/login" className="mt-6 inline-block text-sm font-semibold text-primary hover:underline">
          Volver al inicio de sesión
        </Link>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout title="Recuperar contraseña" subtitle="Te enviaremos un enlace para elegir una nueva.">
      {sent ? (
        <div role="status" className="flex gap-3 rounded-lg bg-success-soft p-4 text-sm text-success">
          <CheckCircle2 className="size-5 shrink-0" aria-hidden />
          Si existe una cuenta con ese correo, recibirás un enlace en unos minutos. Caduca en 1 hora.
        </div>
      ) : (
        <form onSubmit={onSubmit} noValidate className="space-y-4">
          <ServerError message={serverError} />
          <FormField label="Correo electrónico" error={errors.email?.message} required>
            <Input type="email" autoComplete="email" {...register('email')} />
          </FormField>
          <Button type="submit" className="w-full" disabled={isSubmitting}>
            {isSubmitting ? 'Enviando…' : 'Enviar enlace'}
          </Button>
        </form>
      )}
      <Link to="/login" className="mt-6 inline-block text-sm font-semibold text-primary hover:underline">
        Volver al inicio de sesión
      </Link>
    </AuthLayout>
  )
}

const resetSchema = z
  .object({ password: passwordSchema, confirmPassword: z.string() })
  .refine((v) => v.password === v.confirmPassword, {
    path: ['confirmPassword'],
    message: 'Las contraseñas no coinciden.',
  })

export function ResetPasswordPage() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const email = params.get('email') ?? ''
  const token = params.get('token') ?? ''
  const [serverError, setServerError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm({ resolver: zodResolver(resetSchema), defaultValues: { password: '', confirmPassword: '' } })

  const onSubmit = handleSubmit(async (values) => {
    setServerError(null)
    try {
      await api.post('/auth/reset-password', { email, token, newPassword: values.password }, true)
      navigate('/login', { replace: true, state: { reset: true } })
    } catch (error) {
      setServerError(errorMessage(error))
    }
  })

  if (!email || !token) {
    return (
      <AuthLayout title="Enlace no válido" subtitle="Falta información en el enlace de recuperación.">
        <Link to="/recuperar-contrasena" className="text-sm font-semibold text-primary hover:underline">
          Solicitar un enlace nuevo
        </Link>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout title="Elige una contraseña nueva" subtitle={`Para la cuenta ${email}.`}>
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        <ServerError message={serverError} />
        <FormField label="Contraseña nueva" error={errors.password?.message} required>
          <Input type="password" autoComplete="new-password" {...register('password')} />
        </FormField>
        <FormField label="Repite la contraseña" error={errors.confirmPassword?.message} required>
          <Input type="password" autoComplete="new-password" {...register('confirmPassword')} />
        </FormField>
        <Button type="submit" className="w-full" disabled={isSubmitting}>
          {isSubmitting ? 'Guardando…' : 'Guardar contraseña'}
        </Button>
      </form>
    </AuthLayout>
  )
}
