import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/api-client'
import type { LocalDate, MyInvitation, SharedGoal, SharedGoalStatus, SharedGoalSummary } from '@/types/api'

export interface SharedGoalInput {
  name: string
  description: string | null
  unit: string
  collectiveTarget: number
  individualTarget: number | null
  startDate: LocalDate | null
  deadline: LocalDate | null
  version: number | null
}

export function useSharedGoals() {
  return useQuery({
    queryKey: ['shared-goals', 'list'],
    queryFn: () => api.get<SharedGoalSummary[]>('/shared-goals'),
  })
}

export function useSharedGoal(id: string) {
  return useQuery({
    queryKey: ['shared-goals', 'detail', id],
    queryFn: () => api.get<SharedGoal>(`/shared-goals/${id}`),
  })
}

export function useMyInvitations() {
  return useQuery({
    queryKey: ['shared-goals', 'invitations'],
    queryFn: () => api.get<MyInvitation[]>('/shared-goals/invitations'),
  })
}

function useInvalidate() {
  const queryClient = useQueryClient()
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ['shared-goals'] }),
      queryClient.invalidateQueries({ queryKey: ['notifications'] }),
    ])
}

/** All shared-goal writes return fresh data from the server; nothing is computed on the client. */
export function useSharedGoalMutation<TVariables>(fn: (variables: TVariables) => Promise<unknown>) {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: fn, onSuccess: invalidate })
}

export const sharedGoalApi = {
  create: (input: SharedGoalInput) => api.post<SharedGoal>('/shared-goals', input),
  update: (id: string, input: SharedGoalInput) => api.put<SharedGoal>(`/shared-goals/${id}`, input),
  status: (id: string, status: SharedGoalStatus) =>
    api.post<SharedGoal>(`/shared-goals/${id}/status`, { status }),
  invite: (id: string, email: string) => api.post(`/shared-goals/${id}/invitations`, { email }),
  revoke: (id: string, invitationId: string) => api.delete(`/shared-goals/${id}/invitations/${invitationId}`),
  respond: (invitationId: string, accept: boolean) =>
    api.post<{ sharedGoalId: string }>(`/shared-goals/invitations/${invitationId}/respond`, { accept }),
  leave: (id: string) => api.post(`/shared-goals/${id}/leave`),
  removeParticipant: (id: string, participantId: string) =>
    api.delete<SharedGoal>(`/shared-goals/${id}/participants/${participantId}`),
  transferOwnership: (id: string, participantId: string) =>
    api.post<SharedGoal>(`/shared-goals/${id}/owner`, { participantId }),
  contribute: (id: string, input: { date: LocalDate | null; amount: number; note: string | null }) =>
    api.post<SharedGoal>(`/shared-goals/${id}/contributions`, input),
  deleteContribution: (id: string, contributionId: string) =>
    api.delete<SharedGoal>(`/shared-goals/${id}/contributions/${contributionId}`),
}
