import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { server } from '@/test/server'
import { API, renderWithProviders } from '@/test/utils'
import type { Challenge } from '@/types/api'
import { ChallengeDetailPage } from './ChallengesPages'

function challenge(overrides: Partial<Challenge>): Challenge {
  return {
    id: 'c1',
    name: 'Semana constante',
    description: null,
    startDate: '2026-10-13',
    endDate: '2026-10-19',
    rule: 'completionCount',
    targetValue: 5,
    unit: null,
    phase: 'upcoming',
    joinCode: 'ABCD2345',
    isCreator: true,
    canDelete: false,
    canEditRules: false,
    myHabit: null,
    standings: [],
    ...overrides,
  }
}

function renderChallenge(value: Challenge) {
  server.use(http.get(`${API}/challenges/c1`, () => HttpResponse.json(value)))
  return renderWithProviders(<ChallengeDetailPage />, { route: '/retos/c1', path: '/retos/:id' })
}

describe('challenge editing', () => {
  it('with locked rules only the texts change and the original rules are sent back', async () => {
    let sent: unknown = null
    server.use(
      http.put(`${API}/challenges/c1`, async ({ request }) => {
        sent = await request.json()
        return HttpResponse.json(challenge({ name: 'Cinco de siete' }))
      }),
    )
    const user = userEvent.setup()
    renderChallenge(challenge({}))

    await user.click(await screen.findByRole('button', { name: 'Editar' }))
    expect(screen.getByText(/solo puedes cambiar el nombre y la descripción/)).toBeInTheDocument()
    expect(screen.getByLabelText('Empieza')).toBeDisabled()
    expect(screen.getByLabelText(/^Meta/)).toBeDisabled()
    expect(screen.queryByLabelText('Participar con mi hábito')).not.toBeInTheDocument()

    const name = screen.getByLabelText(/^Nombre/)
    await user.clear(name)
    await user.type(name, 'Cinco de siete')
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))

    await waitFor(() => expect(sent).not.toBeNull())
    expect(sent).toEqual({
      name: 'Cinco de siete',
      description: null,
      startDate: '2026-10-13',
      endDate: '2026-10-19',
      rule: 'completionCount',
      targetValue: 5,
      unit: null,
    })
  })

  it('only the creator of an unfinished challenge can edit it', async () => {
    renderChallenge(challenge({ isCreator: false }))
    expect(await screen.findByRole('heading', { name: 'Semana constante' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Editar' })).not.toBeInTheDocument()
  })
})
