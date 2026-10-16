import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { server } from '@/test/server'
import { API, renderWithProviders } from '@/test/utils'
import type { SharedGoal } from '@/types/api'
import { SharedGoalDetailPage } from './SharedGoalsPages'

const progress = (current: number, target: number) => ({
  current,
  target,
  percent: Math.min(100, (current / target) * 100),
  exceededBy: Math.max(0, current - target),
  targetReached: current >= target,
})

function goal(overrides: Partial<SharedGoal>): SharedGoal {
  return {
    id: 'sg1',
    name: 'Leer 20 libros juntos',
    description: null,
    unit: 'libros',
    collectiveTarget: 20,
    individualTarget: null,
    startDate: '2026-10-01',
    deadline: null,
    status: 'active',
    myRole: 'member',
    version: 1,
    collective: progress(5, 20),
    participants: [
      {
        participantId: 'p1',
        displayName: 'Marta',
        role: 'owner',
        isMe: false,
        active: true,
        total: 3,
        individual: null,
      },
      {
        participantId: 'p2',
        displayName: 'Iván',
        role: 'member',
        isMe: true,
        active: true,
        total: 2,
        individual: null,
      },
    ],
    myContributions: [
      { id: 'c1', date: '2026-10-12', amount: 2, note: 'mi nota', createdAt: '2026-10-12T10:00:00Z' },
    ],
    pendingInvitations: [],
    ...overrides,
  }
}

function renderGoal(value: SharedGoal) {
  server.use(http.get(`${API}/shared-goals/sg1`, () => HttpResponse.json(value)))
  return renderWithProviders(<SharedGoalDetailPage />, {
    route: '/compartidos/sg1',
    path: '/compartidos/:id',
  })
}

describe('shared goal permissions in the UI', () => {
  it('members contribute and can leave, but get no owner actions', async () => {
    renderGoal(goal({}))

    expect(await screen.findByRole('heading', { name: 'Leer 20 libros juntos' })).toBeInTheDocument()
    expect(screen.getByRole('progressbar', { name: 'Progreso colectivo' })).toHaveAttribute(
      'aria-valuenow',
      '25',
    )
    expect(screen.getByRole('button', { name: 'Salir' })).toBeInTheDocument()
    expect(screen.getByText('Añadir mi aportación')).toBeInTheDocument()
    expect(screen.getByText(/mi nota/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Invitar' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Completar/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Quitar a/ })).not.toBeInTheDocument()
  })

  it('owners manage invitations and members; closed goals accept no contributions', async () => {
    renderGoal(
      goal({
        myRole: 'owner',
        status: 'completed',
        participants: [
          {
            participantId: 'p1',
            displayName: 'Marta',
            role: 'owner',
            isMe: true,
            active: true,
            total: 3,
            individual: null,
          },
        ],
      }),
    )

    expect(await screen.findByText('Completado')).toBeInTheDocument()
    expect(screen.queryByText('Añadir mi aportación')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Invitar' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Salir' })).not.toBeInTheDocument()
  })

  it('shows invitation management to the owner of an active goal', async () => {
    renderGoal(
      goal({
        myRole: 'owner',
        pendingInvitations: [{ id: 'i1', email: 'ana@test.local', createdAt: '2026-10-12T10:00:00Z' }],
      }),
    )

    expect(await screen.findByRole('button', { name: 'Invitar' })).toBeInTheDocument()
    expect(screen.getByText('ana@test.local')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Quitar a Iván/ })).toBeInTheDocument()
  })

  it('lets the owner hand the goal over to a member after confirming', async () => {
    const asOwner = (role: 'owner' | 'member', other: 'owner' | 'member') =>
      goal({
        myRole: role,
        participants: [
          {
            participantId: 'p1',
            displayName: 'Marta',
            role,
            isMe: true,
            active: true,
            total: 3,
            individual: null,
          },
          {
            participantId: 'p2',
            displayName: 'Iván',
            role: other,
            isMe: false,
            active: true,
            total: 2,
            individual: null,
          },
        ],
      })
    let sent: unknown = null
    server.use(
      http.post(`${API}/shared-goals/sg1/owner`, async ({ request }) => {
        sent = await request.json()
        return HttpResponse.json(asOwner('member', 'owner'))
      }),
    )
    const user = userEvent.setup()
    renderGoal(asOwner('owner', 'member'))

    await user.click(await screen.findByRole('button', { name: 'Ceder la propiedad a Iván' }))
    expect(await screen.findByText('¿Ceder la propiedad a Iván?')).toBeInTheDocument()
    server.use(http.get(`${API}/shared-goals/sg1`, () => HttpResponse.json(asOwner('member', 'owner'))))
    await user.click(screen.getByRole('button', { name: 'Ceder la propiedad' }))

    expect(await screen.findByRole('button', { name: 'Salir' })).toBeInTheDocument()
    expect(sent).toEqual({ participantId: 'p2' })
    expect(screen.queryByRole('button', { name: /Ceder la propiedad/ })).not.toBeInTheDocument()
  })
})
