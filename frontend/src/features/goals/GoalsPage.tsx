import { Plus, Target } from 'lucide-react'
import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { HabitIcon } from '@/components/habit-visuals'
import { EmptyState, ErrorState, PageHeader } from '@/components/page'
import { Button } from '@/components/ui/button'
import { Badge, Card, Skeleton } from '@/components/ui/display'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { formatShortDate } from '@/lib/dates'
import { GOAL_METRIC, GOAL_STATUS } from '@/lib/labels'
import type { Goal } from '@/types/api'
import { useGoals, type GoalFilter } from './api'
import { GoalFormDialog } from './GoalFormDialog'
import { GoalProgressView } from './GoalProgressView'

export function GoalsPage() {
  const [params, setParams] = useSearchParams()
  const filter = (params.get('estado') as GoalFilter | null) ?? 'open'
  const goals = useGoals(filter)
  const [creating, setCreating] = useState(false)

  return (
    <>
      <PageHeader
        title="Objetivos"
        description="Metas medibles, ligadas a tus hábitos o con progreso manual. El progreso lo calcula el servidor."
        actions={
          <Button onClick={() => setCreating(true)}>
            <Plus /> Nuevo objetivo
          </Button>
        }
      />
      <Tabs
        value={filter}
        onValueChange={(value) => setParams(value === 'open' ? {} : { estado: value }, { replace: true })}
        className="mb-5"
      >
        <TabsList aria-label="Estado de los objetivos">
          <TabsTrigger value="open">En curso</TabsTrigger>
          <TabsTrigger value="completed">Completados</TabsTrigger>
          <TabsTrigger value="cancelled">Cancelados</TabsTrigger>
          <TabsTrigger value="all">Todos</TabsTrigger>
        </TabsList>
      </Tabs>

      {goals.isPending ? (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          {Array.from({ length: 4 }, (_, i) => (
            <Skeleton key={i} className="h-40" />
          ))}
        </div>
      ) : goals.isError ? (
        <ErrorState error={goals.error} onRetry={() => goals.refetch()} />
      ) : goals.data.length === 0 ? (
        <EmptyState
          icon={Target}
          title={filter === 'open' ? 'Sin objetivos en curso' : 'No hay objetivos aquí'}
          description="Por ejemplo: «Leer 12 libros este año», «Entrenar 12 veces este mes» o «Meditar 30 días seguidos»."
          action={
            filter === 'open' ? (
              <Button onClick={() => setCreating(true)}>
                <Plus /> Crear objetivo
              </Button>
            ) : undefined
          }
        />
      ) : (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          {goals.data.map((goal) => (
            <GoalCard key={goal.id} goal={goal} />
          ))}
        </div>
      )}
      <GoalFormDialog open={creating} onOpenChange={setCreating} />
    </>
  )
}

function GoalCard({ goal }: { goal: Goal }) {
  const status = GOAL_STATUS[goal.status]
  return (
    <Card className="flex flex-col gap-4 p-5">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <Link
            to={`/objetivos/${goal.id}`}
            className="block truncate font-semibold hover:text-primary hover:underline"
          >
            {goal.name}
          </Link>
          <p className="text-xs text-muted-foreground">
            {GOAL_METRIC[goal.metric].label}
            {goal.deadline ? ` · hasta el ${formatShortDate(goal.deadline)}` : ''}
          </p>
        </div>
        <Badge className={status.className}>{status.label}</Badge>
      </div>
      <GoalProgressView goal={goal} compact />
      {goal.habits.length > 0 ? (
        <div className="flex flex-wrap gap-2">
          {goal.habits.map((h) => (
            <span key={h.id} className="inline-flex items-center gap-1.5 text-xs text-muted-foreground">
              <HabitIcon icon={h.icon} color={h.color} size="sm" className="size-6 [&_svg]:size-3.5" />
              {h.name}
            </span>
          ))}
        </div>
      ) : null}
    </Card>
  )
}
