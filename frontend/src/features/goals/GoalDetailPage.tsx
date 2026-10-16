import { ArrowLeft, Ban, CheckCircle2, Pause, Pencil, Play, RotateCcw, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { toast } from 'sonner'
import { FormField } from '@/components/form-field'
import { HabitIcon } from '@/components/habit-visuals'
import { ErrorState } from '@/components/page'
import { Button } from '@/components/ui/button'
import { Badge, Card, CardContent, CardHeader, CardTitle, Skeleton } from '@/components/ui/display'
import { ConfirmDialog } from '@/components/ui/dialog'
import { Input } from '@/components/ui/form-controls'
import { ChartCard, RunningAreaChart } from '@/features/statistics/charts'
import { errorMessage } from '@/lib/api-client'
import { browserToday, formatDayMonth, formatShortDate } from '@/lib/dates'
import { GOAL_METRIC, GOAL_STATUS } from '@/lib/labels'
import { formatNumber } from '@/lib/utils'
import type { Goal, GoalStatus } from '@/types/api'
import {
  useAddGoalEntry,
  useDeleteGoal,
  useDeleteGoalEntry,
  useGoal,
  useGoalEntries,
  useGoalHistory,
  useGoalStatus,
} from './api'
import { GoalFormDialog } from './GoalFormDialog'
import { GoalProgressView } from './GoalProgressView'

export function GoalDetailPage() {
  const { id = '' } = useParams()
  const goal = useGoal(id)
  if (goal.isPending) return <Skeleton className="h-96" />
  if (goal.isError) return <ErrorState error={goal.error} onRetry={() => goal.refetch()} />
  return <GoalDetail goal={goal.data} />
}

const TRANSITIONS: Record<GoalStatus, { to: GoalStatus; label: string; icon: typeof Play }[]> = {
  active: [
    { to: 'completed', label: 'Marcar como completado', icon: CheckCircle2 },
    { to: 'paused', label: 'Pausar', icon: Pause },
    { to: 'cancelled', label: 'Cancelar', icon: Ban },
  ],
  paused: [
    { to: 'active', label: 'Reanudar', icon: Play },
    { to: 'completed', label: 'Marcar como completado', icon: CheckCircle2 },
    { to: 'cancelled', label: 'Cancelar', icon: Ban },
  ],
  completed: [{ to: 'active', label: 'Reabrir', icon: RotateCcw }],
  cancelled: [],
}

function GoalDetail({ goal }: { goal: Goal }) {
  const navigate = useNavigate()
  const history = useGoalHistory(goal.id)
  const changeStatus = useGoalStatus(goal.id)
  const remove = useDeleteGoal(goal.id)
  const [editing, setEditing] = useState(false)
  const [confirm, setConfirm] = useState<'cancel' | 'delete' | null>(null)
  const status = GOAL_STATUS[goal.status]

  const move = (to: GoalStatus) =>
    changeStatus.mutate(to, {
      onSuccess: () => {
        setConfirm(null)
        toast.success(`Objetivo ${GOAL_STATUS[to].label.toLowerCase()}`)
      },
      onError: (error) => toast.error(errorMessage(error)),
    })

  return (
    <>
      <Link
        to="/objetivos"
        className="mb-4 inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="size-4" aria-hidden /> Objetivos
      </Link>
      <div className="mb-6 flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-2xl font-bold tracking-tight">{goal.name}</h1>
            <Badge className={status.className}>{status.label}</Badge>
          </div>
          <p className="mt-1 text-sm text-muted-foreground">
            {GOAL_METRIC[goal.metric].label} · desde el {formatShortDate(goal.startDate)}
            {goal.deadline ? ` hasta el ${formatShortDate(goal.deadline)}` : ' · sin fecha límite'}
          </p>
          {goal.description ? <p className="mt-2 max-w-2xl text-sm">{goal.description}</p> : null}
        </div>
        <div className="flex flex-wrap gap-2">
          {TRANSITIONS[goal.status].map((t) => (
            <Button
              key={t.to}
              variant={t.to === 'completed' ? 'primary' : 'outline'}
              disabled={changeStatus.isPending}
              onClick={() => (t.to === 'cancelled' ? setConfirm('cancel') : move(t.to))}
            >
              <t.icon /> {t.label}
            </Button>
          ))}
          {goal.status !== 'cancelled' ? (
            <Button variant="outline" onClick={() => setEditing(true)}>
              <Pencil /> Editar
            </Button>
          ) : null}
          <Button variant="ghost" className="text-danger" onClick={() => setConfirm('delete')}>
            <Trash2 /> Eliminar
          </Button>
        </div>
      </div>

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="min-w-0 space-y-6">
          <Card className="p-5">
            <GoalProgressView goal={goal} />
            {goal.progress.targetReached && goal.status === 'active' ? (
              <div className="mt-4 flex flex-wrap items-center justify-between gap-3 rounded-lg bg-success-soft p-3 text-sm text-success">
                <span className="font-semibold">¡Has alcanzado la meta! ¿Lo das por completado?</span>
                <Button size="sm" onClick={() => move('completed')}>
                  <CheckCircle2 /> Completar
                </Button>
              </div>
            ) : null}
          </Card>
          {history.isError ? (
            <ErrorState error={history.error} onRetry={() => history.refetch()} />
          ) : (
            <ChartCard
              title={goal.metric === 'habitStreak' ? 'Racha a lo largo del tiempo' : 'Historial de progreso'}
              subtitle={
                goal.metric === 'habitStreak'
                  ? `Racha en curso cada día (${goal.unit})`
                  : `Progreso acumulado (${goal.unit})`
              }
              rows={history.data ?? []}
              columns={[
                { header: 'Fecha', cell: (r) => formatShortDate(r.date) },
                { header: 'Valor', cell: (r) => formatNumber(r.value), numeric: true },
              ]}
              empty={
                history.isPending
                  ? 'Cargando…'
                  : (history.data?.length ?? 0) === 0
                    ? 'El objetivo aún no ha empezado.'
                    : undefined
              }
            >
              <RunningAreaChart
                data={history.data ?? []}
                xKey="date"
                yKey="value"
                name={goal.unit}
                formatX={formatDayMonth}
                formatValue={(v) => formatNumber(v ?? 0)}
                target={goal.targetValue}
              />
            </ChartCard>
          )}
        </div>
        <aside className="space-y-6">
          {goal.metric === 'manual' ? (
            <ManualEntries goal={goal} />
          ) : (
            <Card>
              <CardHeader>
                <CardTitle>Hábitos vinculados</CardTitle>
              </CardHeader>
              <CardContent>
                <ul className="space-y-2">
                  {goal.habits.map((h) => (
                    <li key={h.id} className="flex items-center gap-2 text-sm">
                      <HabitIcon icon={h.icon} color={h.color} size="sm" />
                      <Link to={`/habitos/${h.id}`} className="hover:underline">
                        {h.name}
                      </Link>
                      {h.isArchived ? (
                        <span className="text-xs text-muted-foreground">(archivado)</span>
                      ) : null}
                    </li>
                  ))}
                </ul>
                <p className="mt-3 text-xs text-muted-foreground">
                  El progreso se actualiza solo al registrar estos hábitos. Cuenta del{' '}
                  {formatShortDate(goal.startDate)} a{' '}
                  {goal.deadline ? `el ${formatShortDate(goal.deadline)}` : 'hoy'}.
                </p>
              </CardContent>
            </Card>
          )}
        </aside>
      </div>

      <GoalFormDialog open={editing} onOpenChange={setEditing} goal={goal} />
      <ConfirmDialog
        open={confirm === 'cancel'}
        onOpenChange={(open) => !open && setConfirm(null)}
        title="¿Cancelar el objetivo?"
        description="Un objetivo cancelado queda archivado tal cual y no se puede reactivar ni editar. Su historial se conserva."
        confirmLabel="Cancelar objetivo"
        pending={changeStatus.isPending}
        onConfirm={() => move('cancelled')}
      />
      <ConfirmDialog
        open={confirm === 'delete'}
        onOpenChange={(open) => !open && setConfirm(null)}
        title="¿Eliminar el objetivo?"
        description="Se borrarán el objetivo y sus registros de progreso manual. Los hábitos vinculados no se tocan. No se puede deshacer."
        confirmLabel="Eliminar"
        pending={remove.isPending}
        onConfirm={() =>
          remove.mutate(undefined, {
            onSuccess: () => {
              toast.success('Objetivo eliminado')
              navigate('/objetivos', { replace: true })
            },
            onError: (error) => toast.error(errorMessage(error)),
          })
        }
      />
    </>
  )
}

function ManualEntries({ goal }: { goal: Goal }) {
  const entries = useGoalEntries(goal.id, true)
  const add = useAddGoalEntry(goal.id)
  const remove = useDeleteGoalEntry(goal.id)
  const [amount, setAmount] = useState('')
  const [date, setDate] = useState(browserToday())
  const [note, setNote] = useState('')
  const [error, setError] = useState<string | null>(null)
  const canAdd = goal.status === 'active'

  const submit = (event: React.FormEvent) => {
    event.preventDefault()
    const value = Number(amount.replace(',', '.'))
    if (!(value > 0) || !/^\d+([.,]\d{1,2})?$/.test(amount.trim())) {
      setError('Indica una cantidad mayor que 0 (2 decimales como máximo).')
      return
    }
    setError(null)
    add.mutate(
      { date, amount: value, note: note.trim() || null },
      {
        onSuccess: () => {
          toast.success('Progreso registrado')
          setAmount('')
          setNote('')
        },
        onError: (e) => toast.error(errorMessage(e)),
      },
    )
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Registrar progreso</CardTitle>
      </CardHeader>
      <CardContent>
        {canAdd ? (
          <form onSubmit={submit} noValidate className="mb-4 space-y-3">
            <div className="grid grid-cols-2 gap-3">
              <FormField label={`Cantidad (${goal.unit})`} error={error ?? undefined}>
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
            <FormField label="Nota (opcional)">
              <Input maxLength={300} value={note} onChange={(e) => setNote(e.target.value)} />
            </FormField>
            <Button type="submit" className="w-full" disabled={add.isPending}>
              {add.isPending ? 'Guardando…' : 'Añadir'}
            </Button>
          </form>
        ) : (
          <p className="mb-4 rounded-lg bg-muted p-3 text-sm text-muted-foreground">
            Solo se puede registrar progreso en objetivos activos.
          </p>
        )}
        {entries.isPending ? (
          <Skeleton className="h-24" />
        ) : entries.isError ? (
          <ErrorState error={entries.error} />
        ) : entries.data.length === 0 ? (
          <p className="text-sm text-muted-foreground">Aún no hay registros.</p>
        ) : (
          <ul className="max-h-80 divide-y divide-border overflow-y-auto text-sm">
            {entries.data.map((entry) => (
              <li key={entry.id} className="flex items-center gap-2 py-2">
                <span className="w-24 shrink-0 text-muted-foreground">{formatShortDate(entry.date)}</span>
                <span className="flex-1 truncate">
                  <strong>+{formatNumber(entry.amount)}</strong> {entry.note ? `· ${entry.note}` : ''}
                </span>
                {goal.status !== 'cancelled' ? (
                  <Button
                    variant="ghost"
                    size="icon"
                    aria-label={`Borrar el registro del ${formatShortDate(entry.date)}`}
                    disabled={remove.isPending}
                    onClick={() =>
                      remove.mutate(entry.id, {
                        onSuccess: () => toast.success('Registro borrado'),
                        onError: (e) => toast.error(errorMessage(e)),
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
  )
}
