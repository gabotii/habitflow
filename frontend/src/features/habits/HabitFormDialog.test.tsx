import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { server } from '@/test/server'
import { API, problem, renderWithProviders } from '@/test/utils'
import { HabitFormDialog } from './HabitFormDialog'

const categories = [
  { id: 'c1', name: 'Salud', color: 'emerald', icon: 'heart', isBuiltIn: true, habitCount: 0 },
  { id: 'c2', name: 'Aprendizaje', color: 'indigo', icon: 'graduation-cap', isBuiltIn: true, habitCount: 0 },
]

function setup() {
  server.use(http.get(`${API}/categories`, () => HttpResponse.json(categories)))
  return renderWithProviders(<HabitFormDialog open onOpenChange={() => {}} />, {
    extraRoutes: [{ path: '/habitos/:id', element: <p>Detalle</p> }],
  })
}

describe('HabitFormDialog', () => {
  it('validates required fields and frequency parameters', async () => {
    const user = userEvent.setup()
    setup()

    await user.click(await screen.findByText('Días concretos'))
    for (const day of ['Lunes', 'Miércoles', 'Viernes']) await user.click(screen.getByLabelText(day))
    await user.click(screen.getByRole('button', { name: 'Crear hábito' }))

    expect(await screen.findByText('Ponle un nombre al hábito.')).toBeInTheDocument()
    expect(screen.getByText('Elige al menos un día.')).toBeInTheDocument()
  })

  it('requires a unit for quantity habits', async () => {
    const user = userEvent.setup()
    setup()

    await user.type(await screen.findByLabelText(/^Nombre/), 'Leer')
    await user.selectOptions(screen.getByLabelText('Tipo'), 'quantity')
    await user.click(screen.getByRole('button', { name: 'Crear hábito' }))

    expect(await screen.findByText(/Indica la unidad/)).toBeInTheDocument()
  })

  it('sends only the parameters of the chosen frequency', async () => {
    let body: Record<string, unknown> | undefined
    server.use(
      http.post(`${API}/habits`, async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>
        return HttpResponse.json({ id: 'new-id' }, { status: 201 })
      }),
    )
    const user = userEvent.setup()
    setup()

    await user.type(await screen.findByLabelText(/^Nombre/), 'Estudiar')
    await user.selectOptions(screen.getByLabelText('Tipo'), 'quantity')
    await user.type(screen.getByLabelText(/^Unidad/), 'minutos')
    await user.type(screen.getByLabelText('Meta diaria'), '30')
    await user.click(screen.getByText('Veces por semana'))
    await user.clear(screen.getByLabelText('Veces por semana'))
    await user.type(screen.getByLabelText('Veces por semana'), '4')
    await user.click(screen.getByRole('button', { name: 'Crear hábito' }))

    await waitFor(() => expect(body).toBeDefined())
    expect(body).toMatchObject({
      name: 'Estudiar',
      type: 'quantity',
      unit: 'minutos',
      categoryId: 'c1',
      schedule: {
        kind: 'timesPerWeek',
        timesPerWeek: 4,
        weekdays: null,
        intervalDays: null,
        targetValue: 30,
      },
    })
    expect(await screen.findByText('Detalle')).toBeInTheDocument()
  })

  it('shows server-side validation errors next to the field', async () => {
    server.use(
      http.post(`${API}/habits`, () =>
        problem(400, 'Revisa los datos enviados.', 'validation_failed', { name: ['Ya usas ese nombre.'] }),
      ),
    )
    const user = userEvent.setup()
    setup()

    await user.type(await screen.findByLabelText(/^Nombre/), 'Duplicado')
    await user.click(screen.getByRole('button', { name: 'Crear hábito' }))

    expect(await screen.findAllByText('Ya usas ese nombre.')).not.toHaveLength(0)
  })
})
