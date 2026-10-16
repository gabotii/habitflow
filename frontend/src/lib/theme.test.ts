import { afterEach, describe, expect, it, vi } from 'vitest'
import { resolveTheme, setThemePreference } from './theme'

describe('theme preference', () => {
  afterEach(() => {
    setThemePreference('system')
    vi.unstubAllGlobals()
  })

  it('applies an explicit choice to <html> and remembers it on this device', () => {
    setThemePreference('dark')
    expect(document.documentElement.dataset.theme).toBe('dark')
    expect(localStorage.getItem('habitflow.theme')).toBe('dark')

    setThemePreference('light')
    expect(document.documentElement.dataset.theme).toBe('light')
    expect(localStorage.getItem('habitflow.theme')).toBe('light')
  })

  it('"system" forgets the choice and follows the OS setting', () => {
    setThemePreference('dark')
    setThemePreference('system')
    expect(localStorage.getItem('habitflow.theme')).toBeNull()

    vi.stubGlobal('matchMedia', (query: string) => ({ matches: query.includes('dark') }))
    expect(resolveTheme('system')).toBe('dark')
    vi.stubGlobal('matchMedia', () => ({ matches: false }))
    expect(resolveTheme('system')).toBe('light')
  })
})
