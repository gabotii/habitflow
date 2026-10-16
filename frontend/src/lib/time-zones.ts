const FALLBACK_ZONES = [
  'UTC',
  'Europe/Madrid',
  'Europe/Lisbon',
  'Europe/London',
  'America/Mexico_City',
  'America/Bogota',
  'America/Lima',
  'America/Santiago',
  'America/Argentina/Buenos_Aires',
  'America/Caracas',
  'America/New_York',
  'Atlantic/Canary',
]

/** The browser's IANA zone, used as the default when registering. */
export function browserTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'Europe/Madrid'
  } catch {
    return 'Europe/Madrid'
  }
}

/** IANA zones the browser knows (falls back to a short list of common Spanish-speaking zones). */
export function timeZoneOptions(current?: string | null): string[] {
  let zones: string[]
  try {
    zones = Intl.supportedValuesOf('timeZone')
  } catch {
    zones = FALLBACK_ZONES
  }
  const set = new Set(['UTC', ...zones])
  if (current) set.add(current)
  set.add(browserTimeZone())
  return [...set].sort((a, b) => (a === 'UTC' ? -1 : b === 'UTC' ? 1 : a.localeCompare(b)))
}
