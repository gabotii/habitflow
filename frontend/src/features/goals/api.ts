import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/api-client'
import type {
  CreateGoalInput,
  Goal,
  GoalEntry,
  GoalHistoryPoint,
  GoalStatus,
  LocalDate,
  UpdateGoalInput,
} from '@/types/api'

export type GoalFilter = 'open' | 'completed' | 'cancelled' | 'all'

export function useGoals(status: GoalFilter) {
  return useQuery({
    queryKey: ['goals', 'list', status],
    queryFn: () => api.get<Goal[]>('/goals', { status }),
  })
}

export function useGoal(id: string) {
  return useQuery({ queryKey: ['goals', 'detail', id], queryFn: () => api.get<Goal>(`/goals/${id}`) })
}

export function useGoalEntries(id: string, enabled: boolean) {
  return useQuery({
    queryKey: ['goals', 'entries', id],
    queryFn: () => api.get<GoalEntry[]>(`/goals/${id}/entries`),
    enabled,
  })
}

export function useGoalHistory(id: string) {
  return useQuery({
    queryKey: ['goals', 'history', id],
    queryFn: () => api.get<GoalHistoryPoint[]>(`/goals/${id}/history`),
  })
}

function useGoalsInvalidation() {
  const queryClient = useQueryClient()
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ['goals'] }),
      queryClient.invalidateQueries({ queryKey: ['dashboard'] }),
      queryClient.invalidateQueries({ queryKey: ['statistics'] }),
    ])
}

export function useCreateGoal() {
  const invalidate = useGoalsInvalidation()
  return useMutation({
    mutationFn: (input: CreateGoalInput) => api.post<Goal>('/goals', input),
    onSuccess: invalidate,
  })
}

export function useUpdateGoal(id: string) {
  const invalidate = useGoalsInvalidation()
  return useMutation({
    mutationFn: (input: UpdateGoalInput) => api.put<Goal>(`/goals/${id}`, input),
    onSuccess: invalidate,
  })
}

export function useGoalStatus(id: string) {
  const invalidate = useGoalsInvalidation()
  return useMutation({
    mutationFn: (status: GoalStatus) => api.post<Goal>(`/goals/${id}/status`, { status }),
    onSuccess: invalidate,
  })
}

export function useDeleteGoal(id: string) {
  const invalidate = useGoalsInvalidation()
  return useMutation({ mutationFn: () => api.delete(`/goals/${id}`), onSuccess: invalidate })
}

export function useAddGoalEntry(id: string) {
  const invalidate = useGoalsInvalidation()
  return useMutation({
    mutationFn: (input: { date: LocalDate | null; amount: number; note: string | null }) =>
      api.post<Goal>(`/goals/${id}/entries`, input),
    onSuccess: invalidate,
  })
}

export function useDeleteGoalEntry(id: string) {
  const invalidate = useGoalsInvalidation()
  return useMutation({
    mutationFn: (entryId: string) => api.delete<Goal>(`/goals/${id}/entries/${entryId}`),
    onSuccess: invalidate,
  })
}
