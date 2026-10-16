import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
  type QueryClient,
} from '@tanstack/react-query'
import { api } from '@/lib/api-client'
import type {
  Category,
  CreateHabitInput,
  Dashboard,
  Habit,
  HabitColor,
  HabitDay,
  HabitListItem,
  HabitLog,
  HabitStatistics,
  LocalDate,
  LogChange,
  LogInput,
  LogResult,
  PagedResult,
  UpdateHabitInput,
} from '@/types/api'

export interface HabitListParams {
  status: 'active' | 'archived' | 'all'
  categoryId?: string
  search?: string
  page: number
  pageSize?: number
}

export const habitKeys = {
  all: ['habits'] as const,
  list: (params: HabitListParams) => ['habits', 'list', params] as const,
  detail: (id: string) => ['habits', 'detail', id] as const,
  logs: (id: string) => ['habits', 'logs', id] as const,
  changes: (id: string, page: number) => ['habits', 'changes', id, page] as const,
  statistics: (id: string, from: string, to: string) => ['habits', 'statistics', id, from, to] as const,
}

export const categoryKeys = { all: ['categories'] as const }

/** Everything derived from logs: refreshed after any write (the server recomputes streaks and stats). */
export function invalidateTracking(queryClient: QueryClient) {
  return Promise.all(
    [['habits'], ['dashboard'], ['calendar'], ['statistics'], ['goals']].map((queryKey) =>
      queryClient.invalidateQueries({ queryKey }),
    ),
  )
}

export function useHabits(params: HabitListParams) {
  return useQuery({
    queryKey: habitKeys.list(params),
    queryFn: ({ signal }) =>
      api.get<PagedResult<HabitListItem>>(
        '/habits',
        {
          status: params.status,
          categoryId: params.categoryId,
          search: params.search,
          page: params.page,
          pageSize: params.pageSize ?? 24,
        },
        signal,
      ),
    placeholderData: keepPreviousData,
  })
}

export function useHabit(id: string) {
  return useQuery({ queryKey: habitKeys.detail(id), queryFn: () => api.get<Habit>(`/habits/${id}`) })
}

export function useHabitLogs(id: string) {
  return useQuery({ queryKey: habitKeys.logs(id), queryFn: () => api.get<HabitLog[]>(`/habits/${id}/logs`) })
}

export function useHabitChanges(id: string, page: number) {
  return useQuery({
    queryKey: habitKeys.changes(id, page),
    queryFn: () => api.get<PagedResult<LogChange>>(`/habits/${id}/logs/changes`, { page, pageSize: 10 }),
    placeholderData: keepPreviousData,
  })
}

export function useHabitStatistics(id: string, from: string, to: string) {
  return useQuery({
    queryKey: habitKeys.statistics(id, from, to),
    queryFn: () => api.get<HabitStatistics>(`/habits/${id}/statistics`, { from, to }),
    placeholderData: keepPreviousData,
  })
}

export function useCategories() {
  return useQuery({
    queryKey: categoryKeys.all,
    queryFn: () => api.get<Category[]>('/categories'),
    staleTime: 300_000,
  })
}

export function useSaveCategory() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, ...body }: { id?: string; name: string; color: HabitColor; icon: string }) =>
      id ? api.put<Category>(`/categories/${id}`, body) : api.post<Category>('/categories', body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: categoryKeys.all }),
  })
}

export function useDeleteCategory() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.delete(`/categories/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: categoryKeys.all }),
  })
}

export function useCreateHabit() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: CreateHabitInput) => api.post<Habit>('/habits', input),
    onSuccess: () => {
      void invalidateTracking(queryClient)
      void queryClient.invalidateQueries({ queryKey: categoryKeys.all })
    },
  })
}

export function useUpdateHabit(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: UpdateHabitInput) => api.put<Habit>(`/habits/${id}`, input),
    onSuccess: (habit) => {
      queryClient.setQueryData(habitKeys.detail(id), habit)
      void invalidateTracking(queryClient)
    },
  })
}

export function useHabitAction(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (action: 'archive' | 'restore' | 'delete'): Promise<void> => {
      if (action === 'delete') await api.delete(`/habits/${id}`)
      else await api.post<Habit>(`/habits/${id}/${action}`)
    },
    onSuccess: () => {
      void invalidateTracking(queryClient)
      void queryClient.invalidateQueries({ queryKey: categoryKeys.all })
    },
  })
}

/** Applies a log result to every cached list that shows this habit, so the card updates instantly. */
function patchHabitDay(
  queryClient: QueryClient,
  habitId: string,
  result: {
    day: Partial<HabitDay> & { date: LocalDate }
    streak?: LogResult['streak']
    week?: LogResult['week']
  },
) {
  const patch = (item: HabitListItem): HabitListItem =>
    item.id !== habitId || item.today.date !== result.day.date
      ? item
      : {
          ...item,
          today: { ...item.today, ...result.day },
          streak: result.streak ?? item.streak,
          week: result.week ?? item.week,
        }

  queryClient.setQueriesData<PagedResult<HabitListItem>>({ queryKey: ['habits', 'list'] }, (data) =>
    data ? { ...data, items: data.items.map(patch) } : data,
  )
  queryClient.setQueryData<Dashboard>(['dashboard'], (data) =>
    data
      ? {
          ...data,
          pending: data.pending.map(patch),
          completed: data.completed.map(patch),
          weekly: data.weekly.map(patch),
        }
      : data,
  )
}

export interface LogVariables {
  habitId: string
  date: LocalDate
  /** null removes the day's log ("unmark"). */
  input: LogInput | null
  /** Optimistic state shown while the request is in flight. */
  optimistic?: { completed: boolean; quantity: number | null; status: LogResult['day']['status'] }
}

/**
 * Writes one day's log. Requests carry absolute values (idempotent PUT), so a retry never double-counts. The card
 * updates optimistically; the server's answer (streak, status) then replaces it and derived views are refreshed.
 */
export function useLogHabit() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ habitId, date, input }: LogVariables) =>
      input === null
        ? api.delete<LogResult>(`/habits/${habitId}/logs/${date}`)
        : api.put<LogResult>(`/habits/${habitId}/logs/${date}`, input),
    onMutate: async ({ habitId, date, optimistic }) => {
      if (!optimistic) return
      await queryClient.cancelQueries({ queryKey: ['habits', 'list'] })
      await queryClient.cancelQueries({ queryKey: ['dashboard'] })
      const previous = {
        lists: queryClient.getQueriesData<PagedResult<HabitListItem>>({ queryKey: ['habits', 'list'] }),
        dashboard: queryClient.getQueryData<Dashboard>(['dashboard']),
      }
      patchHabitDay(queryClient, habitId, {
        day: {
          date,
          status: optimistic.status,
          completed: optimistic.completed,
          quantity: optimistic.quantity,
        },
      })
      return previous
    },
    onError: (_error, _variables, previous) => {
      if (!previous) return
      previous.lists.forEach(([key, data]) => queryClient.setQueryData(key, data))
      queryClient.setQueryData(['dashboard'], previous.dashboard)
    },
    onSuccess: (result, { habitId }) => patchHabitDay(queryClient, habitId, result),
    onSettled: () => invalidateTracking(queryClient),
  })
}
