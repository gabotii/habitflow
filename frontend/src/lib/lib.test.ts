import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { server } from '@/test/server'
import { API, problem } from '@/test/utils'
import { api, ApiError, buildQuery, errorMessage, setUnauthorizedHandler } from './api-client'
import { addLocalDays, daysBetween, formatLongDate, parseLocalDate, toLocalDate } from './dates'
import { describeSchedule } from './labels'
import { formatPercent } from './utils'

describe('api client', () => {
  it('fetches the anti-forgery token first and echoes it on unsafe requests', async () => {
    const seen: (string | null)[] = []
    server.use(
      http.get(`${API}/auth/csrf`, () => {
        document.cookie = 'XSRF-TOKEN=token-1; path=/'
        return new HttpResponse(null, { status: 204 })
      }),
      http.post(`${API}/habits`, ({ request }) => {
        seen.push(request.headers.get('X-XSRF-TOKEN'))
        return HttpResponse.json({ ok: true }, { status: 201 })
      }),
    )

    await api.post('/habits', { name: 'x' })

    expect(seen).toEqual(['token-1'])
  })

  it('renews a stale anti-forgery token once and retries', async () => {
    document.cookie = 'XSRF-TOKEN=stale; path=/'
    const seen: (string | null)[] = []
    server.use(
      http.get(`${API}/auth/csrf`, () => {
        document.cookie = 'XSRF-TOKEN=fresh; path=/'
        return new HttpResponse(null, { status: 204 })
      }),
      http.put(`${API}/habits/1`, ({ request }) => {
        const token = request.headers.get('X-XSRF-TOKEN')
        seen.push(token)
        return token === 'fresh' ? HttpResponse.json({ ok: true }) : problem(403, 'csrf', 'csrf_invalid')
      }),
    )

    await expect(api.put('/habits/1', {})).resolves.toEqual({ ok: true })
    expect(seen).toEqual(['stale', 'fresh'])
  })

  it('reports an expired session and exposes problem details', async () => {
    const onUnauthorized = vi.fn()
    setUnauthorizedHandler(onUnauthorized)
    server.use(http.get(`${API}/dashboard`, () => problem(401, 'Debes iniciar sesión.', 'unauthorized')))

    const error = await api.get('/dashboard').catch((e: unknown) => e)

    expect(onUnauthorized).toHaveBeenCalledOnce()
    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).code).toBe('unauthorized')
    setUnauthorizedHandler(null)
  })

  it('prefers the first field error in messages', () => {
    const error = new ApiError(400, 'Revisa los datos', 'validation_failed', { name: ['Nombre obligatorio'] })
    expect(errorMessage(error)).toBe('Nombre obligatorio')
    expect(errorMessage(new TypeError('fetch failed'))).toMatch(/conectar/)
  })

  it('builds query strings skipping empty values', () => {
    expect(buildQuery({ a: 1, b: '', c: undefined, d: ['x', 'y'] })).toBe('?a=1&d=x&d=y')
  })
})

describe('local dates', () => {
  it('never shift the calendar day through UTC', () => {
    expect(parseLocalDate('2026-03-29').getDate()).toBe(29)
    expect(toLocalDate(parseLocalDate('2026-12-31'))).toBe('2026-12-31')
    expect(addLocalDays('2026-12-31', 1)).toBe('2027-01-01')
    expect(addLocalDays('2028-02-28', 1)).toBe('2028-02-29')
    expect(daysBetween('2026-10-01', '2026-10-12')).toBe(11)
    expect(formatLongDate('2026-10-12')).toBe('lunes, 12 de octubre')
  })
})

describe('labels', () => {
  it('describes every kind of frequency', () => {
    const base = { weekdays: null, timesPerWeek: null, intervalDays: null, targetValue: null }
    expect(describeSchedule({ ...base, kind: 'daily' })).toBe('Todos los días')
    expect(describeSchedule({ ...base, kind: 'weekdays', weekdays: ['monday', 'wednesday', 'friday'] })).toBe(
      'Lun, Mié, Vie',
    )
    expect(
      describeSchedule({
        ...base,
        kind: 'weekdays',
        weekdays: ['monday', 'tuesday', 'wednesday', 'thursday', 'friday'],
      }),
    ).toBe('De lunes a viernes')
    expect(describeSchedule({ ...base, kind: 'timesPerWeek', timesPerWeek: 4 })).toBe('4 veces por semana')
    expect(
      describeSchedule({ ...base, kind: 'everyNDays', intervalDays: 3, targetValue: 20 }, 'páginas'),
    ).toBe('Cada 3 días · meta 20 páginas')
  })

  it('formats percentages and missing values', () => {
    expect(formatPercent(62.5)).toBe('62,5 %')
    expect(formatPercent(null)).toBe('—')
  })
})
