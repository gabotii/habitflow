import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { compliance, dashboard, goal } from '@/test/factories'
import { server } from '@/test/server'
import { API, problem, renderWithProviders } from '@/test/utils'
import type { CalendarDay, Statistics } from '@/types/api'
import { CalendarPage } from './calendar/CalendarPage'
import { DashboardPage } from './dashboard/DashboardPage'
import { GoalProgressView } from './goals/GoalProgressView'
import { StatisticsPage } from './statistics/StatisticsPage'

describe('dashboard', () => {
  it('shows loading, then persisted metrics', async () => {
    server.use(http.get(`${API}/dashboard`, () => HttpResponse.json(dashboard())))
    renderWithProviders(<DashboardPage />)

    expect(await screen.findByText('1 de 2 hábitos de hoy completados')).toBeInTheDocument()
    expect(screen.getByText('Pendientes hoy')).toBeInTheDocument()
    expect(screen.getByRole('progressbar', { name: 'Progreso de Leer 12 libros' })).toHaveAttribute(
      'aria-valuenow',
      '25',
    )
    expect(screen.getByText(/▲ 10 puntos/)).toBeInTheDocument()
  })

  it('guides new users', async () => {
    server.use(http.get(`${API}/dashboard`, () => HttpResponse.json(dashboard({ hasHabits: false }))))
    renderWithProviders(<DashboardPage />)

    expect(await screen.findByText('Te damos la bienvenida a HabitFlow')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Crear hábito/ })).toBeInTheDocument()
  })

  it('shows an error with retry when the API fails', async () => {
    let calls = 0
    server.use(
      http.get(`${API}/dashboard`, () => {
        calls++
        return calls === 1 ? problem(503, 'El servidor no está disponible.') : HttpResponse.json(dashboard())
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<DashboardPage />)

    expect(await screen.findByText('No se pudo cargar la información')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Reintentar' }))
    expect(await screen.findByText('1 de 2 hábitos de hoy completados')).toBeInTheDocument()
  })
})

describe('goal progress', () => {
  it('never draws more than 100 % and states the surplus', () => {
    renderWithProviders(
      <GoalProgressView
        goal={goal({
          progress: { current: 14, target: 12, percent: 100, exceededBy: 2, targetReached: true },
        })}
      />,
    )
    return waitFor(() => {
      expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '100')
      expect(screen.getByText(/\+2 libros sobre la meta/)).toBeInTheDocument()
    })
  })
})

function calendarDays(from: string): CalendarDay[] {
  const start = new Date(`${from}T00:00:00`)
  return Array.from({ length: 42 }, (_, i) => {
    const d = new Date(start)
    d.setDate(start.getDate() + i)
    const date = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
    return { date, state: 'empty', scheduled: 0, completed: 0, extra: 0, weeklyCompletions: 0, percent: null }
  })
}

describe('calendar', () => {
  it('requests the 6-week grid, labels day states and loads the selected day', async () => {
    const requested: string[] = []
    server.use(
      http.get(`${API}/calendar`, ({ request }) => {
        const url = new URL(request.url)
        requested.push(`${url.searchParams.get('from')}..${url.searchParams.get('to')}`)
        const days = calendarDays(url.searchParams.get('from')!)
        const october10 = days.find((d) => d.date === '2026-10-10')
        if (october10) Object.assign(october10, { state: 'missed', scheduled: 2, completed: 0, percent: 0 })
        return HttpResponse.json({ from: days[0].date, to: days[41].date, today: '2026-10-12', days })
      }),
      http.get(`${API}/calendar/:date`, ({ params }) =>
        HttpResponse.json({
          date: params.date,
          today: '2026-10-12',
          summary: {
            date: params.date,
            state: 'missed',
            scheduled: 2,
            completed: 0,
            extra: 0,
            weeklyCompletions: 0,
            percent: 0,
          },
          habits: [],
        }),
      ),
    )
    const user = userEvent.setup()
    renderWithProviders(<CalendarPage />, { route: '/calendario?fecha=2026-10-12', path: '/calendario' })

    const missed = await screen.findByRole('button', { name: /^Sábado, 10 de octubre: Sin cumplir, 0 de 2$/ })
    expect(requested[0]).toBe('2026-09-28..2026-11-08')

    await user.click(missed)
    expect(await screen.findByText('Ningún hábito estaba programado este día.')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Mes siguiente' }))
    await waitFor(() => expect(requested).toContain('2026-10-26..2026-12-06'))
  })
})

describe('statistics', () => {
  const stats: Statistics = {
    from: '2026-10-06',
    to: '2026-10-12',
    summary: compliance({
      combinedRate: 72.5,
      scheduledDayRate: 80,
      expectedOccurrences: 10,
      completedOccurrences: 7,
    }),
    comparison: {
      previousFrom: '2026-09-29',
      previousTo: '2026-10-05',
      previous: compliance(),
      combinedRateDelta: -4.5,
    },
    daily: [{ date: '2026-10-12', scheduled: 2, completed: 1, rate: 50 }],
    weekly: [],
    monthly: [],
    weekdays: [],
    habits: [
      {
        habitId: 'h1',
        name: 'Meditar',
        icon: 'brain',
        color: 'teal',
        type: 'binary',
        unit: null,
        isArchived: false,
        compliance: compliance({ combinedRate: 90, expectedOccurrences: 10, completedOccurrences: 9 }),
        streak: { unit: 'day', current: 3, best: 5 },
      },
    ],
    goals: { active: 1, paused: 0, completed: 2, cancelled: 0, completedInRange: 1 },
  }

  it('shows separate indicators and a table view of each chart', async () => {
    let query = ''
    server.use(
      http.get(`${API}/statistics`, ({ request }) => {
        query = new URL(request.url).search
        return HttpResponse.json(stats)
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<StatisticsPage />, { route: '/estadisticas', path: '/estadisticas' })

    expect(await screen.findByText('72,5 %')).toBeInTheDocument()
    expect(screen.getByText('▼ 4,5 pts')).toBeInTheDocument()
    expect(query).toBe('?days=30')

    await user.click(screen.getByRole('button', { name: 'Ver tabla: Cumplimiento diario' }))
    const table = screen.getByRole('table', { name: 'Cumplimiento diario' })
    expect(within(table).getByText('1 de 2')).toBeInTheDocument()
    expect(within(table).getByText('50 %')).toBeInTheDocument()
  })
})
