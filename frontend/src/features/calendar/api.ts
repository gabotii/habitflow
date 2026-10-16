import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api } from '@/lib/api-client'
import type { CalendarDayDetail, CalendarRange, CalendarWeek, LocalDate } from '@/types/api'

export function useCalendarRange(from: LocalDate, to: LocalDate) {
  return useQuery({
    queryKey: ['calendar', 'range', from, to],
    queryFn: () => api.get<CalendarRange>('/calendar', { from, to }),
    placeholderData: keepPreviousData,
  })
}

export function useCalendarWeek(date: LocalDate) {
  return useQuery({
    queryKey: ['calendar', 'week', date],
    queryFn: () => api.get<CalendarWeek>(`/calendar/week/${date}`),
    placeholderData: keepPreviousData,
  })
}

export function useCalendarDay(date: LocalDate | null) {
  return useQuery({
    queryKey: ['calendar', 'day', date],
    queryFn: () => api.get<CalendarDayDetail>(`/calendar/${date}`),
    enabled: date !== null,
  })
}
