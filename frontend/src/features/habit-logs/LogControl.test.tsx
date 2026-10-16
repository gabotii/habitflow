import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { day, TODAY } from '@/test/factories'
import { server } from '@/test/server'
import { API, problem, renderWithProviders } from '@/test/utils'
import { LogControl } from './LogControl'

const result = (overrides: object) => ({
  log: null,
  day: day(overrides),
  streak: { unit: 'day', current: 4, best: 7 },
  week: null,
})

describe('LogControl', () => {
  it('marks a yes/no habit as done with an idempotent PUT of the absolute state', async () => {
    let body: unknown
    server.use(
      http.put(`${API}/habits/h1/logs/${TODAY}`, async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(result({ status: 'completed', completed: true }))
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(
      <LogControl habit={{ id: 'h1', name: 'Meditar', type: 'binary', unit: null }} day={day()} />,
    )

    await user.click(await screen.findByRole('button', { name: 'Marcar «Meditar» como completado' }))

    await waitFor(() => expect(body).toEqual({ completed: true }))
  })

  it('sends the new daily total for quantity habits, never the increment', async () => {
    const bodies: unknown[] = []
    server.use(
      http.put(`${API}/habits/h2/logs/${TODAY}`, async ({ request }) => {
        bodies.push(await request.json())
        return HttpResponse.json(result({ quantity: 16, target: 20, status: 'partial' }))
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(
      <LogControl
        habit={{ id: 'h2', name: 'Leer', type: 'quantity', unit: 'páginas' }}
        day={day({ quantity: 15, target: 20, status: 'partial' })}
      />,
    )

    await user.click(await screen.findByRole('button', { name: /Sumar 1 páginas a «Leer»/ }))

    await waitFor(() => expect(bodies).toEqual([{ quantity: 16 }]))
  })

  it('reports server errors (e.g. future dates) to the user', async () => {
    server.use(
      http.put(`${API}/habits/h1/logs/${TODAY}`, () =>
        problem(422, 'No puedes registrar progreso en días futuros.', 'log.future_date'),
      ),
    )
    const user = userEvent.setup()
    renderWithProviders(
      <LogControl habit={{ id: 'h1', name: 'Meditar', type: 'binary', unit: null }} day={day()} />,
    )

    await user.click(await screen.findByRole('button', { name: 'Marcar «Meditar» como completado' }))

    expect(await screen.findByText('No puedes registrar progreso en días futuros.')).toBeInTheDocument()
  })

  it('opens the dialog to correct a day with a note', async () => {
    let body: unknown
    server.use(
      http.put(`${API}/habits/h1/logs/2026-10-10`, async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(result({ date: '2026-10-10', completed: true, status: 'completed' }))
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(
      <LogControl
        habit={{ id: 'h1', name: 'Meditar', type: 'binary', unit: null }}
        day={day({ date: '2026-10-10', status: 'missed' })}
        isToday={false}
      />,
    )

    await user.click(await screen.findByRole('button', { name: 'Añadir nota o corregir «Meditar»' }))
    expect(await screen.findByRole('dialog')).toHaveTextContent('Sábado, 10 de octubre')
    await user.click(screen.getByText('Sí, completado'))
    await user.type(screen.getByLabelText(/Nota/), 'Por la noche')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))

    await waitFor(() => expect(body).toEqual({ completed: true, note: 'Por la noche' }))
  })
})
