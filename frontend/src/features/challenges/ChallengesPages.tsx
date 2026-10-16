import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft, Copy, LogIn, LogOut, Pencil, Plus, Trash2, Trophy, Users } from 'lucide-react'
import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { Link, useNavigate, useParams } from 'react-router'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormField } from '@/components/form-field'
import { ProgressBar } from '@/components/habit-visuals'
import { EmptyState, ErrorState, PageHeader } from '@/components/page'
import { Button } from '@/components/ui/button'
import { Badge, Card, CardContent, CardHeader, CardTitle, Skeleton } from '@/components/ui/display'
import { ConfirmDialog, Dialog, DialogContent } from '@/components/ui/dialog'
import { Input, Select, Textarea } from '@/components/ui/form-controls'
import { useHabits } from '@/features/habits/api'
import { errorMessage } from '@/lib/api-client'
import { addLocalDays, browserToday, formatShortDate, isLocalDate } from '@/lib/dates'
import { cn, formatNumber } from '@/lib/utils'
import type { Challenge, ChallengePhase, ChallengeResult, ChallengeRule } from '@/types/api'
import {
  useChallenge,
  useChallengePreview,
  useChallenges,
  useCreateChallenge,
  useDeleteChallenge,
  useJoinChallenge,
  useLeaveChallenge,
  useUpdateChallenge,
} from './api'

const PHASE: Record<ChallengePhase, { label: string; className: string }> = {
  upcoming: { label: 'Próximo', className: 'bg-muted text-muted-foreground' },
  active: { label: 'En curso', className: 'bg-primary-soft text-primary' },
  finished: {
    label: 'Terminado',
    className: 'bg-slate-200 text-slate-800 dark:bg-slate-700 dark:text-slate-100',
  },
}

const RESULT: Record<ChallengeResult, { label: string; className: string }> = {
  inProgress: { label: 'En progreso', className: 'bg-primary-soft text-primary' },
  completed: { label: 'Superado', className: 'bg-success-soft text-success' },
  notCompleted: { label: 'No superado', className: 'bg-danger-soft text-danger' },
  abandoned: { label: 'Abandonado', className: 'bg-muted text-muted-foreground' },
}

export function describeRule(rule: ChallengeRule, target: number, unit: string | null): string {
  switch (rule) {
    case 'completionCount':
      return `Completar el hábito ${formatNumber(target)} veces`
    case 'quantityTotal':
      return `Acumular ${formatNumber(target)} ${unit ?? ''}`.trim()
    default:
      return 'Completar el hábito todos los días'
  }
}

export function ChallengesPage() {
  const challenges = useChallenges()
  const [creating, setCreating] = useState(false)
  const [joining, setJoining] = useState(false)

  return (
    <>
      <PageHeader
        title="Retos"
        description="Retos cortos con otras personas. Cada cual avanza con su propio hábito; nadie ve tus hábitos ni tus notas."
        actions={
          <>
            <Button variant="outline" onClick={() => setJoining(true)}>
              <LogIn /> Unirme con un código
            </Button>
            <Button onClick={() => setCreating(true)}>
              <Plus /> Crear reto
            </Button>
          </>
        }
      />
      {challenges.isPending ? (
        <Skeleton className="h-40" />
      ) : challenges.isError ? (
        <ErrorState error={challenges.error} onRetry={() => challenges.refetch()} />
      ) : challenges.data.length === 0 ? (
        <EmptyState
          icon={Trophy}
          title="Sin retos todavía"
          description="Crea uno (por ejemplo, «Leer 100 páginas esta semana») y comparte el código, o únete al de otra persona."
        />
      ) : (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          {challenges.data.map((c) => (
            <Card key={c.id} className="flex flex-col gap-3 p-5">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <Link
                    to={`/retos/${c.id}`}
                    className="block truncate font-semibold hover:text-primary hover:underline"
                  >
                    {c.name}
                  </Link>
                  <p className="text-xs text-muted-foreground">
                    {describeRule(c.rule, c.targetValue, c.unit)} · {formatShortDate(c.startDate)} –{' '}
                    {formatShortDate(c.endDate)}
                  </p>
                </div>
                <Badge className={PHASE[c.phase].className}>{PHASE[c.phase].label}</Badge>
              </div>
              {c.me ? (
                <div>
                  <div className="mb-1 flex justify-between text-sm">
                    <span>
                      Tu progreso: <strong>{formatNumber(c.me.progress)}</strong> /{' '}
                      {formatNumber(c.targetValue)}
                    </span>
                    <Badge className={RESULT[c.me.result].className}>{RESULT[c.me.result].label}</Badge>
                  </div>
                  <ProgressBar
                    value={c.me.percent}
                    label={`Tu progreso en ${c.name}`}
                    tone={c.me.result === 'completed' ? 'success' : 'primary'}
                  />
                </div>
              ) : (
                <p className="text-sm text-muted-foreground">Lo creaste, pero aún no participas.</p>
              )}
              <p className="flex items-center gap-1 text-xs text-muted-foreground">
                <Users className="size-3.5" aria-hidden /> {c.participants} participantes
              </p>
            </Card>
          ))}
        </div>
      )}
      {creating ? <ChallengeFormDialog onClose={() => setCreating(false)} /> : null}
      {joining ? <JoinChallengeDialog onClose={() => setJoining(false)} /> : null}
    </>
  )
}

const createSchema = z
  .object({
    name: z.string().trim().min(1, 'Ponle un nombre.').max(80, 'Máximo 80 caracteres.'),
    description: z.string().max(500, 'Máximo 500 caracteres.'),
    startDate: z.string().refine(isLocalDate, 'Fecha no válida.'),
    endDate: z.string().refine(isLocalDate, 'Fecha no válida.'),
    rule: z.enum(['completionCount', 'quantityTotal', 'dailyCompletion'] satisfies ChallengeRule[]),
    targetValue: z.string(),
    unit: z.string().trim().max(24),
    habitId: z.string(),
  })
  .superRefine((v, ctx) => {
    if (v.endDate < v.startDate)
      ctx.addIssue({ code: 'custom', path: ['endDate'], message: 'Debe ser posterior al inicio.' })
    if (v.rule !== 'dailyCompletion' && !(Number(v.targetValue.replace(',', '.')) > 0))
      ctx.addIssue({ code: 'custom', path: ['targetValue'], message: 'Indica una meta mayor que 0.' })
    if (v.rule === 'quantityTotal' && !v.unit)
      ctx.addIssue({ code: 'custom', path: ['unit'], message: 'Indica la unidad.' })
  })

/**
 * Creates a challenge or edits one (creator only). When editing, the rules (dates, rule, target, unit) stay
 * read-only unless the API says they can still change (before the start, nobody else joined).
 */
function ChallengeFormDialog({ challenge, onClose }: { challenge?: Challenge; onClose: () => void }) {
  const navigate = useNavigate()
  const create = useCreateChallenge()
  const update = useUpdateChallenge(challenge?.id ?? '')
  const habits = useHabits({ status: 'active', page: 1, pageSize: 100 })
  const today = browserToday()
  const rulesLocked = challenge !== undefined && !challenge.canEditRules
  const {
    register,
    control,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm({
    resolver: zodResolver(createSchema),
    defaultValues: challenge
      ? {
          name: challenge.name,
          description: challenge.description ?? '',
          startDate: challenge.startDate,
          endDate: challenge.endDate,
          rule: challenge.rule,
          targetValue: String(challenge.targetValue),
          unit: challenge.unit ?? '',
          habitId: '',
        }
      : {
          name: '',
          description: '',
          startDate: today,
          endDate: addLocalDays(today, 6),
          rule: 'completionCount' as ChallengeRule,
          targetValue: '5',
          unit: '',
          habitId: '',
        },
  })
  const rule = useWatch({ control, name: 'rule' })

  const onSubmit = handleSubmit(async (v) => {
    const texts = { name: v.name.trim(), description: v.description.trim() || null }
    // Locked rules are sent back unchanged: only the texts can change.
    const rules = rulesLocked
      ? {
          startDate: challenge.startDate,
          endDate: challenge.endDate,
          rule: challenge.rule,
          targetValue: challenge.targetValue,
          unit: challenge.unit,
        }
      : {
          startDate: v.startDate,
          endDate: v.endDate,
          rule: v.rule,
          targetValue: v.rule === 'dailyCompletion' ? null : Number(v.targetValue.replace(',', '.')),
          unit: v.rule === 'quantityTotal' ? v.unit.trim() : null,
        }
    try {
      if (challenge) {
        await update.mutateAsync({ ...texts, ...rules })
        toast.success('Reto actualizado')
        onClose()
      } else {
        const created = await create.mutateAsync({ ...texts, ...rules, habitId: v.habitId || null })
        toast.success('Reto creado. Comparte el código para que se unan.')
        navigate(`/retos/${created.id}`)
      }
    } catch (error) {
      toast.error(errorMessage(error))
    }
  })

  const submitLabel = challenge
    ? isSubmitting
      ? 'Guardando…'
      : 'Guardar cambios'
    : isSubmitting
      ? 'Creando…'
      : 'Crear reto'

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={challenge ? 'Editar reto' : 'Crear reto'}
        description={
          rulesLocked
            ? 'El reto ya ha empezado o se ha unido alguien: solo puedes cambiar el nombre y la descripción.'
            : 'Hasta 31 días. Las reglas se pueden cambiar hasta que empiece, mientras nadie más se haya unido.'
        }
        footer={
          <>
            <Button variant="outline" onClick={onClose}>
              Cancelar
            </Button>
            <Button type="submit" form="challenge-form" disabled={isSubmitting}>
              {submitLabel}
            </Button>
          </>
        }
      >
        <form id="challenge-form" onSubmit={onSubmit} noValidate className="grid gap-4">
          <FormField label="Nombre" error={errors.name?.message} required>
            <Input placeholder="Semana lectora" autoFocus {...register('name')} />
          </FormField>
          <FormField label="Descripción" error={errors.description?.message}>
            <Textarea rows={2} {...register('description')} />
          </FormField>
          <fieldset disabled={rulesLocked} className="contents">
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <FormField label="Empieza" error={errors.startDate?.message}>
                <Input type="date" min={today} {...register('startDate')} />
              </FormField>
              <FormField label="Termina" error={errors.endDate?.message}>
                <Input type="date" min={today} {...register('endDate')} />
              </FormField>
            </div>
            <FormField label="Regla">
              <Select {...register('rule')}>
                <option value="completionCount">Completar un hábito N veces</option>
                <option value="quantityTotal">Acumular una cantidad (páginas, minutos…)</option>
                <option value="dailyCompletion">Completar un hábito todos los días</option>
              </Select>
            </FormField>
            {rule !== 'dailyCompletion' ? (
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <FormField label="Meta" error={errors.targetValue?.message} required>
                  <Input inputMode="decimal" {...register('targetValue')} />
                </FormField>
                {rule === 'quantityTotal' ? (
                  <FormField
                    label="Unidad"
                    error={errors.unit?.message}
                    hint="Debe coincidir con la de los hábitos."
                    required
                  >
                    <Input placeholder="páginas" {...register('unit')} />
                  </FormField>
                ) : null}
              </div>
            ) : null}
          </fieldset>
          {challenge ? null : (
            <FormField label="Participar con mi hábito" hint="Opcional. Tu progreso saldrá de este hábito.">
              <Select {...register('habitId')}>
                <option value="">Solo organizo, no participo (todavía)</option>
                {habits.data?.items
                  .filter((h) => rule !== 'quantityTotal' || h.type === 'quantity')
                  .map((h) => (
                    <option key={h.id} value={h.id}>
                      {h.name}
                      {h.unit ? ` (${h.unit})` : ''}
                    </option>
                  ))}
              </Select>
            </FormField>
          )}
        </form>
      </DialogContent>
    </Dialog>
  )
}

function JoinChallengeDialog({ onClose, initialCode = '' }: { onClose: () => void; initialCode?: string }) {
  const navigate = useNavigate()
  const [code, setCode] = useState(initialCode)
  const [habitId, setHabitId] = useState('')
  const normalized = code.trim().toUpperCase()
  const preview = useChallengePreview(normalized.length === 8 ? normalized : null)
  const habits = useHabits({ status: 'active', page: 1, pageSize: 100 })
  const join = useJoinChallenge()
  const eligible = (habits.data?.items ?? []).filter(
    (h) =>
      preview.data?.rule !== 'quantityTotal' ||
      (h.type === 'quantity' && h.unit?.toLowerCase() === preview.data.unit?.toLowerCase()),
  )

  const submit = () =>
    join.mutate(
      { code: normalized, habitId },
      {
        onSuccess: (challenge) => {
          toast.success('Te has unido al reto')
          navigate(`/retos/${challenge.id}`)
        },
        onError: (error) => toast.error(errorMessage(error)),
      },
    )

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title="Unirme a un reto"
        footer={
          <>
            <Button variant="outline" onClick={onClose}>
              Cancelar
            </Button>
            <Button
              onClick={submit}
              disabled={!preview.data || preview.data.alreadyJoined || !habitId || join.isPending}
            >
              Unirme
            </Button>
          </>
        }
      >
        <div className="grid gap-4">
          <FormField label="Código del reto" hint="8 caracteres, te lo pasa quien lo organiza.">
            <Input
              value={code}
              maxLength={8}
              autoFocus
              className="font-mono tracking-widest uppercase"
              onChange={(e) => setCode(e.target.value)}
            />
          </FormField>
          {preview.isError ? <p className="text-sm text-danger">No hay ningún reto con ese código.</p> : null}
          {preview.data ? (
            <div className="rounded-lg bg-muted p-3 text-sm">
              <p className="font-semibold">{preview.data.name}</p>
              <p className="text-muted-foreground">
                {describeRule(preview.data.rule, preview.data.targetValue, preview.data.unit)} ·{' '}
                {formatShortDate(preview.data.startDate)} – {formatShortDate(preview.data.endDate)} ·{' '}
                {preview.data.participants} participantes
              </p>
              {preview.data.alreadyJoined ? (
                <p className="mt-1 font-semibold">Ya participas en este reto.</p>
              ) : null}
              {preview.data.phase === 'finished' ? (
                <p className="mt-1 font-semibold text-danger">Este reto ya terminó.</p>
              ) : null}
            </div>
          ) : null}
          {preview.data && !preview.data.alreadyJoined ? (
            <FormField
              label="¿Con qué hábito participas?"
              hint="Solo cuentan tus registros de ese hábito. Nadie más verá cuál es."
            >
              <Select value={habitId} onChange={(e) => setHabitId(e.target.value)}>
                <option value="">Elige un hábito</option>
                {eligible.map((h) => (
                  <option key={h.id} value={h.id}>
                    {h.name}
                  </option>
                ))}
              </Select>
            </FormField>
          ) : null}
        </div>
      </DialogContent>
    </Dialog>
  )
}

export function ChallengeDetailPage() {
  const { id = '' } = useParams()
  const challenge = useChallenge(id)
  if (challenge.isPending) return <Skeleton className="h-96" />
  if (challenge.isError) return <ErrorState error={challenge.error} onRetry={() => challenge.refetch()} />
  return <ChallengeDetail challenge={challenge.data} />
}

function ChallengeDetail({ challenge }: { challenge: Challenge }) {
  const navigate = useNavigate()
  const leave = useLeaveChallenge(challenge.id)
  const remove = useDeleteChallenge(challenge.id)
  const [confirm, setConfirm] = useState<'leave' | 'delete' | null>(null)
  const [editing, setEditing] = useState(false)
  const me = challenge.standings.find((s) => s.isMe)

  return (
    <>
      <Link
        to="/retos"
        className="mb-4 inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="size-4" aria-hidden /> Retos
      </Link>
      <div className="mb-6 flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-2xl font-bold tracking-tight">{challenge.name}</h1>
            <Badge className={PHASE[challenge.phase].className}>{PHASE[challenge.phase].label}</Badge>
          </div>
          <p className="mt-1 text-sm text-muted-foreground">
            {describeRule(challenge.rule, challenge.targetValue, challenge.unit)} · del{' '}
            {formatShortDate(challenge.startDate)} al {formatShortDate(challenge.endDate)}
          </p>
          {challenge.description ? <p className="mt-2 max-w-2xl text-sm">{challenge.description}</p> : null}
        </div>
        <div className="flex flex-wrap gap-2">
          {challenge.isCreator && challenge.phase !== 'finished' ? (
            <Button variant="outline" onClick={() => setEditing(true)}>
              <Pencil /> Editar
            </Button>
          ) : null}
          {me && me.result !== 'abandoned' && challenge.phase !== 'finished' ? (
            <Button variant="outline" onClick={() => setConfirm('leave')}>
              <LogOut /> Abandonar
            </Button>
          ) : null}
          {challenge.canDelete ? (
            <Button variant="ghost" className="text-danger" onClick={() => setConfirm('delete')}>
              <Trash2 /> Eliminar
            </Button>
          ) : null}
        </div>
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-[minmax(0,1fr)_20rem]">
        <Card>
          <CardHeader>
            <CardTitle>Clasificación</CardTitle>
          </CardHeader>
          <CardContent>
            {challenge.standings.length === 0 ? (
              <p className="text-sm text-muted-foreground">Aún no participa nadie.</p>
            ) : (
              <ol className="space-y-4">
                {challenge.standings.map((s, i) => (
                  <li
                    key={`${s.displayName}-${i}`}
                    className={cn('rounded-lg p-2', s.isMe && 'bg-primary-soft/60')}
                  >
                    <div className="mb-1 flex flex-wrap items-center justify-between gap-2 text-sm">
                      <span className="font-medium">
                        {i + 1}. {s.displayName}
                        {s.isMe ? ' (tú)' : ''}
                      </span>
                      <span className="flex items-center gap-2">
                        <span>
                          {formatNumber(s.progress)} / {formatNumber(challenge.targetValue)}
                        </span>
                        <Badge className={RESULT[s.result].className}>{RESULT[s.result].label}</Badge>
                      </span>
                    </div>
                    <ProgressBar
                      value={s.percent}
                      label={`Progreso de ${s.displayName}`}
                      tone={s.result === 'completed' ? 'success' : 'primary'}
                    />
                  </li>
                ))}
              </ol>
            )}
            <p className="mt-4 text-xs text-muted-foreground">
              El progreso de cada persona sale de sus propios registros: nadie puede modificar el de otra. Al
              abandonar, el progreso se congela.
            </p>
          </CardContent>
        </Card>
        <aside className="space-y-6">
          <Card>
            <CardHeader>
              <CardTitle>Código para invitar</CardTitle>
            </CardHeader>
            <CardContent>
              <div className="flex items-center gap-2">
                <code className="flex-1 rounded-lg bg-muted px-3 py-2 text-center font-mono text-lg tracking-widest">
                  {challenge.joinCode}
                </code>
                <Button
                  variant="outline"
                  size="icon"
                  aria-label="Copiar código"
                  onClick={() =>
                    navigator.clipboard
                      .writeText(challenge.joinCode)
                      .then(() => toast.success('Código copiado'))
                      .catch(() => toast.error('No se pudo copiar'))
                  }
                >
                  <Copy />
                </Button>
              </div>
            </CardContent>
          </Card>
          {challenge.myHabit ? (
            <Card>
              <CardHeader>
                <CardTitle>Participas con</CardTitle>
              </CardHeader>
              <CardContent>
                <Link
                  to={`/habitos/${challenge.myHabit.id}`}
                  className="font-medium text-primary hover:underline"
                >
                  {challenge.myHabit.name}
                </Link>
                <p className="mt-1 text-xs text-muted-foreground">Solo tú ves qué hábito usas.</p>
              </CardContent>
            </Card>
          ) : null}
        </aside>
      </div>

      {editing ? <ChallengeFormDialog challenge={challenge} onClose={() => setEditing(false)} /> : null}
      <ConfirmDialog
        open={confirm === 'leave'}
        onOpenChange={(open) => !open && setConfirm(null)}
        title="¿Abandonar el reto?"
        description="Tu resultado quedará como «Abandonado» y tus registros dejarán de contar. No podrás volver a unirte."
        confirmLabel="Abandonar"
        pending={leave.isPending}
        onConfirm={() =>
          leave.mutate(undefined, {
            onSuccess: () => {
              setConfirm(null)
              toast.success('Has abandonado el reto')
            },
            onError: (e) => toast.error(errorMessage(e)),
          })
        }
      />
      <ConfirmDialog
        open={confirm === 'delete'}
        onOpenChange={(open) => !open && setConfirm(null)}
        title="¿Eliminar el reto?"
        description="Solo es posible mientras nadie más se haya unido."
        confirmLabel="Eliminar"
        pending={remove.isPending}
        onConfirm={() =>
          remove.mutate(undefined, {
            onSuccess: () => {
              toast.success('Reto eliminado')
              navigate('/retos', { replace: true })
            },
            onError: (e) => toast.error(errorMessage(e)),
          })
        }
      />
    </>
  )
}
