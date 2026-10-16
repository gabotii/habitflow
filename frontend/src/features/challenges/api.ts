import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/api-client'
import type { Challenge, ChallengePreview, ChallengeRule, ChallengeSummary, LocalDate } from '@/types/api'

export interface CreateChallengeInput {
  name: string
  description: string | null
  startDate: LocalDate
  endDate: LocalDate
  rule: ChallengeRule
  targetValue: number | null
  unit: string | null
  habitId: string | null
}

export function useChallenges() {
  return useQuery({
    queryKey: ['challenges', 'list'],
    queryFn: () => api.get<ChallengeSummary[]>('/challenges'),
  })
}

export function useChallenge(id: string) {
  return useQuery({
    queryKey: ['challenges', 'detail', id],
    queryFn: () => api.get<Challenge>(`/challenges/${id}`),
  })
}

export function useChallengePreview(code: string | null) {
  return useQuery({
    queryKey: ['challenges', 'preview', code],
    queryFn: () => api.get<ChallengePreview>(`/challenges/code/${encodeURIComponent(code!)}`),
    enabled: code !== null && code.length === 8,
    retry: false,
  })
}

function useInvalidate() {
  const queryClient = useQueryClient()
  return () => queryClient.invalidateQueries({ queryKey: ['challenges'] })
}

export function useCreateChallenge() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: (input: CreateChallengeInput) => api.post<Challenge>('/challenges', input),
    onSuccess: invalidate,
  })
}

export function useJoinChallenge() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: (input: { code: string; habitId: string }) => api.post<Challenge>('/challenges/join', input),
    onSuccess: invalidate,
  })
}

export function useLeaveChallenge(id: string) {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: () => api.post<Challenge>(`/challenges/${id}/leave`),
    onSuccess: invalidate,
  })
}

export function useDeleteChallenge(id: string) {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: () => api.delete(`/challenges/${id}`), onSuccess: invalidate })
}

export type UpdateChallengeInput = Omit<CreateChallengeInput, 'habitId'>

export function useUpdateChallenge(id: string) {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: (input: UpdateChallengeInput) => api.put<Challenge>(`/challenges/${id}`, input),
    onSuccess: invalidate,
  })
}
