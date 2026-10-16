import { useSyncExternalStore } from 'react'

/**
 * Light/dark theme. The preference is per device (localStorage); "system" follows the OS setting live.
 * public/theme-init.js applies the same logic before the first paint, so keep the two in sync.
 */
export type ThemePreference = 'system' | 'light' | 'dark'

const STORAGE_KEY = 'habitflow.theme'
const THEME_COLOR = { light: '#4f46e5', dark: '#0f0e1a' } as const
const listeners = new Set<() => void>()

function readPreference(): ThemePreference {
  try {
    const value = localStorage.getItem(STORAGE_KEY)
    return value === 'light' || value === 'dark' ? value : 'system'
  } catch {
    return 'system'
  }
}

let preference: ThemePreference = readPreference()

function systemQuery(): MediaQueryList | null {
  return typeof window.matchMedia === 'function' ? window.matchMedia('(prefers-color-scheme: dark)') : null
}

export function resolveTheme(value: ThemePreference): 'light' | 'dark' {
  if (value !== 'system') return value
  return systemQuery()?.matches ? 'dark' : 'light'
}

function apply() {
  const theme = resolveTheme(preference)
  document.documentElement.dataset.theme = theme
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', THEME_COLOR[theme])
  listeners.forEach((listener) => listener())
}

export function setThemePreference(value: ThemePreference) {
  preference = value
  try {
    if (value === 'system') localStorage.removeItem(STORAGE_KEY)
    else localStorage.setItem(STORAGE_KEY, value)
  } catch {
    // Storage blocked (private mode): the choice still applies for this visit.
  }
  apply()
}

systemQuery()?.addEventListener('change', () => {
  if (preference === 'system') apply()
})

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function useTheme() {
  const current = useSyncExternalStore(subscribe, () => preference)
  return { preference: current, resolved: resolveTheme(current), setPreference: setThemePreference }
}
