import { zodResolver } from '@hookform/resolvers/zod'
import {
  ArrowLeft,
  Ban,
  CheckCircle2,
  Crown,
  Handshake,
  LogOut,
  Mail,
  Pencil,
  Plus,
  Trash2,
  UserMinus,
} from 'lucide-react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, useNavigate, useParams } from 'react-router'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormField } from '@/components/form-field'
import { ProgressBar } from '@/components/habit-visuals'
import { EmptyState, ErrorState, PageHeader } from '@/components/page'
import { Button } from '@/components/ui/button'
import { Badge, Card, CardContent, CardHeader, CardTitle, Skeleton } from '@/components/ui/display'
import { ConfirmDialog, Dialog, DialogContent } from '@/components/ui/dialog'
import { Input, Textarea } from '@/components/ui/form-controls'
import { errorMessage } from '@/lib/api-client'
import { browserToday, formatShortDate, formatRelative } from '@/lib/dates'
import { formatNumber } from '@/lib/utils'
import type { ProgressValue, SharedGoal, SharedGoalStatus, SharedParticipant } from '@/types/api'
import { sharedGoalApi, useMyInvitations, useSharedGoal, useSharedGoalMutation, useSharedGoals } from './api'

const STATUS: Record<SharedGoalStatus, { label: string; className: string }> = {
  active: { label: 'Activo', className: 'bg-primary-soft text-primary' },
  completed: { label: 'Completado', className: 'bg-success-soft text-success' },
  cancelled: { label: 'Cancelado', className: 'bg-muted text-muted-foreground' },
}

function Progress({ value, unit, label }: { value: ProgressValue; unit: string; label: string }) {
  return (
    <div>
      <div className="mb-1 flex justify-between gap-2 text-sm">
        <span>
          <strong>{formatNumber(value.current)}</strong> / {formatNumber(value.target)} {unit}
        </span>
        <span className="font-semibold">{formatNumber(value.percent)} %</span>
      </div>
      <ProgressBar value={value.percent} label={label} tone={value.targetReached ? 'success' : 'primary'} />
      {value.exceededBy > 0 ? (
        <p className="mt-1 text-xs font-semibold text-success">
          +{formatNumber(value.exceededBy)} {unit} sobre la meta
        </p>
      ) : null}
    </div>
  )
}

export function SharedGoalsPage() {
  const goals = useSharedGoals()
  const invitations = useMyInvitations()
  const navigate = useNavigate()
  const respond = useSharedGoalMutation(({ id, accept }: { id: string; accept: boolean }) =>
    sharedGoalApi.respond(id, accept),
  )
  const [creating, setCreating] = useState(false)

  return (
    <>
      <PageHeader
        title="Objetivos compartidos"
        description="Metas en equipo por invitación. Cada persona ve el total de los demás, nunca sus hábitos ni sus notas."
        actions={
          <Button onClick={() => setCreating(true)}>
            <Plus /> Nuevo objetivo compartido
          </Button>
        }
      />

      {invitations.data && invitations.data.length > 0 ? (
        <Card className="mb-6 border-primary/30">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Mail className="size-4 text-primary" aria-hidden /> Invitaciones pendientes
            </CardTitle>
          </CardHeader>
          <CardContent>
            <ul className="divide-y divide-border">
              {invitations.data.map((inv) => (
                <li
                  key={inv.id}
                  className="flex flex-col gap-2 py-3 sm:flex-row sm:items-center sm:justify-between"
                >
                  <span className="text-sm">
                    <strong>{inv.invitedBy}</strong> te invita a «{inv.goalName}» ·{' '}
                    {formatRelative(inv.createdAt)}
                  </span>
                  <span className="flex gap-2">
                    <Button
                      size="sm"
                      disabled={respond.isPending}
                      onClick={() =>
                        respond.mutate(
                          { id: inv.id, accept: true },
                          {
                            onSuccess: (result) => {
                              toast.success('Te has unido al objetivo')
                              navigate(`/compartidos/${(result as { sharedGoalId: string }).sharedGoalId}`)
                            },
                            onError: (e) => toast.error(errorMessage(e)),
                          },
                        )
                      }
                    >
                      Aceptar
                    </Button>
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={respond.isPending}
                      onClick={() =>
                        respond.mutate(
                          { id: inv.id, accept: false },
                          {
                            onSuccess: () => toast.success('Invitación rechazada'),
                            onError: (e) => toast.error(errorMessage(e)),
                          },
                        )
                      }
                    >
                      Rechazar
                    </Button>
                  </span>
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      ) : null}

      {goals.isPending ? (
        <Skeleton className="h-40" />
      ) : goals.isError ? (
        <ErrorState error={goals.error} onRetry={() => goals.refetch()} />
      ) : goals.data.length === 0 ? (
        <EmptyState
          icon={Handshake}
          title="Sin objetivos compartidos"
          description="Por ejemplo, «Leer 50 libros en familia» o «Correr 500 km en equipo». Invita por correo a quien quieras."
        />
      ) : (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          {goals.data.map((g) => (
            <Card key={g.id} className="flex flex-col gap-3 p-5">
              <div className="flex items-start justify-between gap-3">
                <Link
                  to={`/compartidos/${g.id}`}
                  className="min-w-0 truncate font-semibold hover:text-primary hover:underline"
                >
                  {g.name}
                </Link>
                <Badge className={STATUS[g.status].className}>{STATUS[g.status].label}</Badge>
              </div>
              <Progress value={g.collective} unit={g.unit} label={`Progreso colectivo de ${g.name}`} />
              <p className="text-xs text-muted-foreground">
                {g.participants} participantes · tu aportación: {formatNumber(g.myTotal)} {g.unit} ·{' '}
                {g.myRole === 'owner' ? 'propietario/a' : 'miembro'}
              </p>
            </Card>
          ))}
        </div>
      )}
      {creating ? <SharedGoalFormDialog onClose={() => setCreating(false)} /> : null}
    </>
  )
}

const goalSchema = z.object({
  name: z.string().trim().min(1, 'Ponle un nombre.').max(100, 'Máximo 100 caracteres.'),
  description: z.string().max(1000),
  unit: z.string().trim().min(1, 'Indica la unidad.').max(24),
  collectiveTarget: z.string().refine((v) => Number(v.replace(',', '.')) > 0, 'Indica una meta mayor que 0.'),
  individualTarget: z
    .string()
    .refine((v) => v === '' || Number(v.replace(',', '.')) > 0, 'Debe ser mayor que 0.'),
  deadline: z.string(),
})

function SharedGoalFormDialog({ onClose, goal }: { onClose: () => void; goal?: SharedGoal }) {
  const navigate = useNavigate()
  const save = useSharedGoalMutation((input: Parameters<typeof sharedGoalApi.create>[0]) =>
    goal ? sharedGoalApi.update(goal.id, input) : sharedGoalApi.create(input),
  )
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm({
    resolver: zodResolver(goalSchema),
    defaultValues: {
      name: goal?.name ?? '',
      description: goal?.description ?? '',
      unit: goal?.unit ?? '',
      collectiveTarget: goal ? String(goal.collectiveTarget) : '',
      individualTarget: goal?.individualTarget ? String(goal.individualTarget) : '',
      deadline: goal?.deadline ?? '',
    },
  })

  const onSubmit = handleSubmit(async (v) => {
    try {
      const result = (await save.mutateAsync({
        name: v.name.trim(),
        description: v.description.trim() || null,
        unit: v.unit.trim(),
        collectiveTarget: Number(v.collectiveTarget.replace(',', '.')),
        individualTarget: v.individualTarget ? Number(v.individualTarget.replace(',', '.')) : null,
        startDate: goal?.startDate ?? null,
        deadline: v.deadline || null,
        version: goal?.version ?? null,
      })) as SharedGoal
      toast.success(goal ? 'Objetivo actualizado' : 'Objetivo compartido creado. Ahora invita a tu equipo.')
      onClose()
      if (!goal) navigate(`/compartidos/${result.id}`)
    } catch (error) {
      toast.error(errorMessage(error))
    }
  })

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={goal ? 'Editar objetivo compartido' : 'Nuevo objetivo compartido'}
        footer={
          <>
            <Button variant="outline" onClick={onClose}>
              Cancelar
            </Button>
            <Button type="submit" form="shared-goal-form" disabled={isSubmitting}>
              {isSubmitting ? 'Guardando…' : 'Guardar'}
            </Button>
          </>
        }
      >
        <form id="shared-goal-form" onSubmit={onSubmit} noValidate className="grid gap-4">
          <FormField label="Nombre" error={errors.name?.message} required>
            <Input autoFocus placeholder="Leer 50 libros en familia" {...register('name')} />
          </FormField>
          <FormField label="Descripción" error={errors.description?.message}>
            <Textarea rows={2} {...register('description')} />
          </FormField>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <FormField label="Unidad" error={errors.unit?.message} required>
              <Input placeholder="libros" {...register('unit')} />
            </FormField>
            <FormField label="Meta colectiva" error={errors.collectiveTarget?.message} required>
              <Input inputMode="decimal" {...register('collectiveTarget')} />
            </FormField>
            <FormField label="Meta individual" error={errors.individualTarget?.message} hint="Opcional.">
              <Input inputMode="decimal" {...register('individualTarget')} />
            </FormField>
          </div>
          <FormField label="Fecha límite" hint="Opcional.">
            <Input type="date" {...register('deadline')} />
          </FormField>
        </form>
      </DialogContent>
    </Dialog>
  )
}

export function SharedGoalDetailPage() {
  const { id = '' } = useParams()
  const goal = useSharedGoal(id)
  if (goal.isPending) return <Skeleton className="h-96" />
  if (goal.isError) return <ErrorState error={goal.error} onRetry={() => goal.refetch()} />
  return <SharedGoalDetail goal={goal.data} />
}

function SharedGoalDetail({ goal }: { goal: SharedGoal }) {
  const navigate = useNavigate()
  const owner = goal.myRole === 'owner'
  const active = goal.status === 'active'
  const [editing, setEditing] = useState(false)
  const [confirm, setConfirm] = useState<'leave' | 'completed' | 'cancelled' | null>(null)
  const [successor, setSuccessor] = useState<SharedParticipant | null>(null)
  const [email, setEmail] = useState('')
  const [amount, setAmount] = useState('')
  const [date, setDate] = useState(browserToday())
  const [note, setNote] = useState('')

  const invite = useSharedGoalMutation((value: string) => sharedGoalApi.invite(goal.id, value))
  const revoke = useSharedGoalMutation((invitationId: string) => sharedGoalApi.revoke(goal.id, invitationId))
  const status = useSharedGoalMutation((value: SharedGoalStatus) => sharedGoalApi.status(goal.id, value))
  const leave = useSharedGoalMutation(() => sharedGoalApi.leave(goal.id))
  const removeParticipant = useSharedGoalMutation((participantId: string) =>
    sharedGoalApi.removeParticipant(goal.id, participantId),
  )
  const transfer = useSharedGoalMutation((participantId: string) =>
    sharedGoalApi.transferOwnership(goal.id, participantId),
  )
  const contribute = useSharedGoalMutation((input: { date: string; amount: number; note: string | null }) =>
    sharedGoalApi.contribute(goal.id, input),
  )
  const deleteContribution = useSharedGoalMutation((contributionId: string) =>
    sharedGoalApi.deleteContribution(goal.id, contributionId),
  )
  const fail = (e: unknown) => toast.error(errorMessage(e))

  return (
    <>
      <Link
        to="/compartidos"
        className="mb-4 inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="size-4" aria-hidden /> Objetivos compartidos
      </Link>
      <div className="mb-6 flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-2xl font-bold tracking-tight">{goal.name}</h1>
            <Badge className={STATUS[goal.status].className}>{STATUS[goal.status].label}</Badge>
          </div>
          <p className="mt-1 text-sm text-muted-foreground">
            Desde el {formatShortDate(goal.startDate)}
            {goal.deadline ? ` hasta el ${formatShortDate(goal.deadline)}` : ''} ·{' '}
            {owner ? 'Eres la persona propietaria' : 'Participas como miembro'}
          </p>
          {goal.description ? <p className="mt-2 max-w-2xl text-sm">{goal.description}</p> : null}
        </div>
        <div className="flex flex-wrap gap-2">
          {owner && active ? (
            <>
              <Button onClick={() => setConfirm('completed')}>
                <CheckCircle2 /> Completar
              </Button>
              <Button variant="outline" onClick={() => setEditing(true)}>
                <Pencil /> Editar
              </Button>
              <Button variant="ghost" className="text-danger" onClick={() => setConfirm('cancelled')}>
                <Ban /> Cancelar objetivo
              </Button>
            </>
          ) : null}
          {!owner ? (
            <Button variant="outline" onClick={() => setConfirm('leave')}>
              <LogOut /> Salir
            </Button>
          ) : null}
        </div>
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="min-w-0 space-y-6">
          <Card className="p-5">
            <h2 className="mb-3 font-semibold">Progreso colectivo</h2>
            <Progress value={goal.collective} unit={goal.unit} label="Progreso colectivo" />
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>Participantes</CardTitle>
            </CardHeader>
            <CardContent>
              <ul className="space-y-4">
                {goal.participants.map((p) => (
                  <li key={p.participantId}>
                    <div className="mb-1 flex flex-wrap items-center justify-between gap-2 text-sm">
                      <span className="font-medium">
                        {p.displayName}
                        {p.isMe ? ' (tú)' : ''}
                        {p.role === 'owner' ? (
                          <Badge className="ml-2 bg-muted text-muted-foreground">Propietario/a</Badge>
                        ) : null}
                        {!p.active ? (
                          <Badge className="ml-2 bg-muted text-muted-foreground">Salió</Badge>
                        ) : null}
                      </span>
                      <span className="flex items-center gap-2">
                        {formatNumber(p.total)} {goal.unit}
                        {owner && p.active && p.role !== 'owner' && active ? (
                          <Button
                            variant="ghost"
                            size="icon"
                            aria-label={`Ceder la propiedad a ${p.displayName}`}
                            title="Ceder la propiedad"
                            onClick={() => setSuccessor(p)}
                          >
                            <Crown />
                          </Button>
                        ) : null}
                        {owner && p.active && p.role !== 'owner' && active ? (
                          <Button
                            variant="ghost"
                            size="icon"
                            aria-label={`Quitar a ${p.displayName}`}
                            onClick={() =>
                              removeParticipant.mutate(p.participantId, {
                                onSuccess: () => toast.success('Participante quitado'),
                                onError: fail,
                              })
                            }
                          >
                            <UserMinus />
                          </Button>
                        ) : null}
                      </span>
                    </div>
                    {p.individual ? (
                      <ProgressBar
                        value={p.individual.percent}
                        label={`Meta individual de ${p.displayName}`}
                        tone={p.individual.targetReached ? 'success' : 'primary'}
                      />
                    ) : null}
                  </li>
                ))}
              </ul>
              <p className="mt-4 text-xs text-muted-foreground">
                Solo se comparten nombres y totales. Las notas de cada aportación son privadas de quien la
                hizo.
              </p>
            </CardContent>
          </Card>
        </div>
        <aside className="space-y-6">
          {active ? (
            <Card>
              <CardHeader>
                <CardTitle>Añadir mi aportación</CardTitle>
              </CardHeader>
              <CardContent>
                <form
                  className="space-y-3"
                  onSubmit={(e) => {
                    e.preventDefault()
                    const value = Number(amount.replace(',', '.'))
                    if (!(value > 0)) return toast.error('Indica una cantidad mayor que 0.')
                    contribute.mutate(
                      { date, amount: value, note: note.trim() || null },
                      {
                        onSuccess: () => {
                          toast.success('Aportación registrada')
                          setAmount('')
                          setNote('')
                        },
                        onError: fail,
                      },
                    )
                  }}
                >
                  <div className="grid grid-cols-2 gap-3">
                    <FormField label={`Cantidad (${goal.unit})`}>
                      <Input inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
                    </FormField>
                    <FormField label="Fecha">
                      <Input
                        type="date"
                        value={date}
                        min={goal.startDate}
                        onChange={(e) => setDate(e.target.value)}
                      />
                    </FormField>
                  </div>
                  <FormField label="Nota privada (opcional)">
                    <Input maxLength={300} value={note} onChange={(e) => setNote(e.target.value)} />
                  </FormField>
                  <Button type="submit" className="w-full" disabled={contribute.isPending}>
                    Añadir
                  </Button>
                </form>
              </CardContent>
            </Card>
          ) : null}
          <Card>
            <CardHeader>
              <CardTitle>Mis aportaciones</CardTitle>
            </CardHeader>
            <CardContent>
              {goal.myContributions.length === 0 ? (
                <p className="text-sm text-muted-foreground">Aún no has aportado.</p>
              ) : (
                <ul className="max-h-72 divide-y divide-border overflow-y-auto text-sm">
                  {goal.myContributions.map((c) => (
                    <li key={c.id} className="flex items-center gap-2 py-2">
                      <span className="w-24 shrink-0 text-muted-foreground">{formatShortDate(c.date)}</span>
                      <span className="flex-1 truncate">
                        <strong>+{formatNumber(c.amount)}</strong> {c.note ? `· ${c.note}` : ''}
                      </span>
                      {active ? (
                        <Button
                          variant="ghost"
                          size="icon"
                          aria-label={`Borrar la aportación del ${formatShortDate(c.date)}`}
                          onClick={() =>
                            deleteContribution.mutate(c.id, {
                              onSuccess: () => toast.success('Aportación borrada'),
                              onError: fail,
                            })
                          }
                        >
                          <Trash2 />
                        </Button>
                      ) : null}
                    </li>
                  ))}
                </ul>
              )}
            </CardContent>
          </Card>
          {owner && active ? (
            <Card>
              <CardHeader>
                <CardTitle>Invitar</CardTitle>
              </CardHeader>
              <CardContent>
                <form
                  className="flex gap-2"
                  onSubmit={(e) => {
                    e.preventDefault()
                    if (!/^\S+@\S+\.\S+$/.test(email.trim()))
                      return toast.error('Introduce un correo válido.')
                    invite.mutate(email.trim(), {
                      onSuccess: () => {
                        toast.success('Invitación registrada. Si la persona tiene cuenta, la verá al entrar.')
                        setEmail('')
                      },
                      onError: fail,
                    })
                  }}
                >
                  <Input
                    type="email"
                    aria-label="Correo de la persona invitada"
                    placeholder="correo@ejemplo.com"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                  />
                  <Button type="submit" disabled={invite.isPending}>
                    Invitar
                  </Button>
                </form>
                {goal.pendingInvitations.length > 0 ? (
                  <ul className="mt-3 divide-y divide-border text-sm">
                    {goal.pendingInvitations.map((inv) => (
                      <li key={inv.id} className="flex items-center justify-between gap-2 py-2">
                        <span className="truncate">{inv.email}</span>
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() =>
                            revoke.mutate(inv.id, {
                              onSuccess: () => toast.success('Invitación retirada'),
                              onError: fail,
                            })
                          }
                        >
                          Retirar
                        </Button>
                      </li>
                    ))}
                  </ul>
                ) : null}
              </CardContent>
            </Card>
          ) : null}
        </aside>
      </div>

      {editing ? <SharedGoalFormDialog goal={goal} onClose={() => setEditing(false)} /> : null}
      <ConfirmDialog
        open={confirm === 'leave'}
        onOpenChange={(open) => !open && setConfirm(null)}
        title="¿Salir del objetivo?"
        description="Dejarás de verlo. Tus aportaciones pasadas seguirán contando para el grupo."
        confirmLabel="Salir"
        pending={leave.isPending}
        onConfirm={() =>
          leave.mutate(undefined, {
            onSuccess: () => {
              toast.success('Has salido del objetivo')
              navigate('/compartidos', { replace: true })
            },
            onError: fail,
          })
        }
      />
      <ConfirmDialog
        open={successor !== null}
        onOpenChange={(open) => !open && setSuccessor(null)}
        title={`¿Ceder la propiedad a ${successor?.displayName ?? ''}?`}
        description="Podrá editar el objetivo, invitar y quitar participantes, y cerrarlo. Tú seguirás como miembro y podrás salir cuando quieras."
        confirmLabel="Ceder la propiedad"
        pending={transfer.isPending}
        onConfirm={() =>
          successor &&
          transfer.mutate(successor.participantId, {
            onSuccess: () => {
              toast.success(`${successor.displayName} gestiona ahora el objetivo`)
              setSuccessor(null)
            },
            onError: fail,
          })
        }
      />
      <ConfirmDialog
        open={confirm === 'completed' || confirm === 'cancelled'}
        onOpenChange={(open) => !open && setConfirm(null)}
        title={confirm === 'completed' ? '¿Dar el objetivo por completado?' : '¿Cancelar el objetivo?'}
        description="Quedará cerrado para todo el grupo: no se podrán añadir más aportaciones."
        confirmLabel={confirm === 'completed' ? 'Completar' : 'Cancelar objetivo'}
        destructive={confirm === 'cancelled'}
        pending={status.isPending}
        onConfirm={() =>
          confirm &&
          confirm !== 'leave' &&
          status.mutate(confirm, {
            onSuccess: () => {
              setConfirm(null)
              toast.success('Objetivo cerrado')
            },
            onError: fail,
          })
        }
      />
    </>
  )
}
