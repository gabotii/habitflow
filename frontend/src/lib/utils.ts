import { clsx, type ClassValue } from 'clsx'
import { twMerge } from 'tailwind-merge'

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

/** Turns empty strings from form inputs into null, as the API expects for optional fields. */
export function emptyToNull(value: string | null | undefined): string | null {
  const trimmed = value?.trim()
  return trimmed ? trimmed : null
}

const numberFormat = new Intl.NumberFormat('es-ES', { maximumFractionDigits: 2 })
const compactFormat = new Intl.NumberFormat('es-ES', { notation: 'compact', maximumFractionDigits: 1 })

export function formatNumber(value: number): string {
  return numberFormat.format(value)
}

export function formatCompact(value: number): string {
  return Math.abs(value) >= 10_000 ? compactFormat.format(value) : numberFormat.format(value)
}

/** "72,5 %" or an em dash when there is nothing to measure. */
export function formatPercent(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : `${numberFormat.format(value)} %`
}
