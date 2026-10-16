import { addDays, differenceInCalendarDays, format, isValid, parse } from 'date-fns'
import { es } from 'date-fns/locale'
import type { Instant, LocalDate } from '@/types/api'

/**
 * Local calendar dates travel as "yyyy-MM-dd" and must never go through UTC conversion (that shifts the day in
 * zones west of Greenwich). These helpers parse them as wall-clock dates.
 */
export function parseLocalDate(value: LocalDate): Date {
  return parse(value, 'yyyy-MM-dd', new Date(2000, 0, 1))
}

export function toLocalDate(date: Date): LocalDate {
  return format(date, 'yyyy-MM-dd')
}

export function isLocalDate(value: string): boolean {
  return /^\d{4}-\d{2}-\d{2}$/.test(value) && isValid(parseLocalDate(value))
}

export function addLocalDays(value: LocalDate, days: number): LocalDate {
  return toLocalDate(addDays(parseLocalDate(value), days))
}

export function daysBetween(from: LocalDate, to: LocalDate): number {
  return differenceInCalendarDays(parseLocalDate(to), parseLocalDate(from))
}

/** "lunes, 12 de octubre" */
export function formatLongDate(value: LocalDate): string {
  return format(parseLocalDate(value), "EEEE, d 'de' MMMM", { locale: es })
}

/** "12 oct 2026" */
export function formatShortDate(value: LocalDate): string {
  return format(parseLocalDate(value), 'd MMM yyyy', { locale: es })
}

/** "12 oct" */
export function formatDayMonth(value: LocalDate): string {
  return format(parseLocalDate(value), 'd MMM', { locale: es })
}

/** "lun 12" */
export function formatWeekdayDay(value: LocalDate): string {
  return format(parseLocalDate(value), 'EEE d', { locale: es })
}

/** An instant shown in a given IANA zone (defaults to the browser's). */
export function formatInstant(
  value: Instant,
  timeZone?: string,
  options?: Intl.DateTimeFormatOptions,
): string {
  return new Intl.DateTimeFormat('es-ES', {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone,
    ...options,
  }).format(new Date(value))
}

/** Relative "hace 5 min" style label for activity feeds. */
export function formatRelative(value: Instant, now: Date = new Date()): string {
  const seconds = Math.round((new Date(value).getTime() - now.getTime()) / 1000)
  const rtf = new Intl.RelativeTimeFormat('es-ES', { numeric: 'auto' })
  const abs = Math.abs(seconds)
  if (abs < 60) return rtf.format(seconds, 'second')
  if (abs < 3600) return rtf.format(Math.round(seconds / 60), 'minute')
  if (abs < 86_400) return rtf.format(Math.round(seconds / 3600), 'hour')
  return rtf.format(Math.round(seconds / 86_400), 'day')
}

/** Today in the browser's zone; only a fallback before the server's "today" (user zone) is known. */
export function browserToday(): LocalDate {
  return toLocalDate(new Date())
}

export function capitalize(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1)
}
