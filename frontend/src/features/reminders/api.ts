import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/api-client'
import type { AppNotification, PagedResult, Reminder, ReminderInput } from '@/types/api'

export function useReminders() {
  return useQuery({ queryKey: ['reminders'], queryFn: () => api.get<Reminder[]>('/reminders') })
}

function useInvalidate() {
  const queryClient = useQueryClient()
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ['reminders'] }),
      queryClient.invalidateQueries({ queryKey: ['dashboard'] }),
    ])
}

export function useSaveReminder(id?: string) {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: (input: ReminderInput) =>
      id ? api.put<Reminder>(`/reminders/${id}`, input) : api.post<Reminder>('/reminders', input),
    onSuccess: invalidate,
  })
}

export function useToggleReminder() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) =>
      api.post<Reminder>(`/reminders/${id}/active`, { isActive }),
    onSuccess: invalidate,
  })
}

export function useDeleteReminder() {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: (id: string) => api.delete(`/reminders/${id}`), onSuccess: invalidate })
}

/** In-app notifications are polled (there is no push channel): once a minute while the app is open. */
export function useUnreadCount() {
  return useQuery({
    queryKey: ['notifications', 'unread'],
    queryFn: () => api.get<{ count: number }>('/notifications/unread-count'),
    refetchInterval: 60_000,
  })
}

export function useNotifications(enabled: boolean) {
  return useQuery({
    queryKey: ['notifications', 'list'],
    queryFn: () => api.get<PagedResult<AppNotification>>('/notifications', { pageSize: 20 }),
    enabled,
  })
}

export function useMarkNotifications() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string | 'all') =>
      id === 'all' ? api.post('/notifications/read-all') : api.post(`/notifications/${id}/read`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['notifications'] }),
  })
}
