import { Plus, Search, Sprout, Tags } from 'lucide-react'
import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { EmptyState, ErrorState, PageHeader, Pagination } from '@/components/page'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/display'
import { Input, Select } from '@/components/ui/form-controls'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useDebouncedValue } from '@/hooks/use-debounced-value'
import { useCategories, useHabits, type HabitListParams } from './api'
import { CategoriesDialog } from './CategoriesDialog'
import { HabitCard } from './HabitCard'
import { HabitFormDialog } from './HabitFormDialog'

export function HabitsPage() {
  const [params, setParams] = useSearchParams()
  const status = (params.get('estado') as HabitListParams['status'] | null) ?? 'active'
  const categoryId = params.get('categoria') ?? undefined
  const page = Number(params.get('pagina') ?? '1') || 1
  const [search, setSearch] = useState(params.get('q') ?? '')
  const debouncedSearch = useDebouncedValue(search, 300)
  const [creating, setCreating] = useState(false)
  const [managingCategories, setManagingCategories] = useState(false)

  const categories = useCategories()
  const habits = useHabits({ status, categoryId, search: debouncedSearch.trim() || undefined, page })

  const update = (changes: Record<string, string | undefined>) => {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) {
      if (value) next.set(key, value)
      else next.delete(key)
    }
    if (!('pagina' in changes)) next.delete('pagina')
    setParams(next, { replace: true })
  }

  const filtered = Boolean(categoryId || debouncedSearch.trim())

  return (
    <>
      <PageHeader
        title="Hábitos"
        description="Crea, ajusta y registra tus hábitos. Archivar conserva todo el historial."
        actions={
          <>
            <Button variant="outline" onClick={() => setManagingCategories(true)}>
              <Tags /> Categorías
            </Button>
            <Button onClick={() => setCreating(true)}>
              <Plus /> Nuevo hábito
            </Button>
          </>
        }
      />

      <div className="mb-5 flex flex-col gap-3 lg:flex-row lg:items-center">
        <Tabs
          value={status}
          onValueChange={(value) => update({ estado: value === 'active' ? undefined : value })}
        >
          <TabsList aria-label="Estado">
            <TabsTrigger value="active">Activos</TabsTrigger>
            <TabsTrigger value="archived">Archivados</TabsTrigger>
            <TabsTrigger value="all">Todos</TabsTrigger>
          </TabsList>
        </Tabs>
        <div className="flex flex-1 flex-col gap-3 sm:flex-row">
          <div className="relative flex-1">
            <Search
              className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground"
              aria-hidden
            />
            <Input
              type="search"
              aria-label="Buscar hábitos"
              placeholder="Buscar por nombre…"
              className="pl-9"
              value={search}
              onChange={(e) => {
                setSearch(e.target.value)
                update({ q: e.target.value || undefined })
              }}
            />
          </div>
          <Select
            aria-label="Filtrar por categoría"
            className="sm:w-56"
            value={categoryId ?? ''}
            onChange={(e) => update({ categoria: e.target.value || undefined })}
          >
            <option value="">Todas las categorías</option>
            {categories.data?.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </Select>
        </div>
      </div>

      {habits.isPending ? (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {Array.from({ length: 6 }, (_, i) => (
            <Skeleton key={i} className="h-36" />
          ))}
          <span className="sr-only" role="status">
            Cargando hábitos…
          </span>
        </div>
      ) : habits.isError ? (
        <ErrorState error={habits.error} onRetry={() => habits.refetch()} />
      ) : habits.data.items.length === 0 ? (
        filtered || status !== 'active' ? (
          <EmptyState
            icon={Search}
            title="No hay hábitos que coincidan"
            description="Prueba con otro filtro o búsqueda."
          />
        ) : (
          <EmptyState
            icon={Sprout}
            title="Tu primer hábito empieza aquí"
            description="Elige algo pequeño y concreto, como «Leer 10 páginas» o «Caminar 15 minutos». Podrás ajustar la frecuencia cuando quieras."
            action={
              <Button onClick={() => setCreating(true)}>
                <Plus /> Crear un hábito
              </Button>
            }
          />
        )
      ) : (
        <div className={habits.isPlaceholderData ? 'opacity-60 transition-opacity' : undefined}>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {habits.data.items.map((habit) => (
              <HabitCard key={habit.id} habit={habit} />
            ))}
          </div>
          <Pagination
            page={habits.data.page}
            totalPages={habits.data.totalPages}
            totalCount={habits.data.totalCount}
            onPageChange={(next) => update({ pagina: String(next) })}
          />
        </div>
      )}

      <HabitFormDialog open={creating} onOpenChange={setCreating} />
      <CategoriesDialog open={managingCategories} onOpenChange={setManagingCategories} />
    </>
  )
}
