import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { server } from '@/test/server'
import { API, problem, renderWithProviders, testUser } from '@/test/utils'
import { LoginPage, RegisterPage } from './AuthPages'
import { RedirectIfAuthenticated, RequireAuth } from './RequireAuth'

describe('protected routes', () => {
  it('redirect anonymous visitors to the login screen', async () => {
    renderWithProviders(
      <RequireAuth>
        <p>Contenido privado</p>
      </RequireAuth>,
      { route: '/habitos', path: '/habitos', signedIn: false },
    )

    expect(await screen.findByText('Pantalla de login')).toBeInTheDocument()
    expect(screen.queryByText('Contenido privado')).not.toBeInTheDocument()
  })

  it('show private content when the session cookie is valid', async () => {
    renderWithProviders(<p>Contenido privado</p>, { route: '/habitos', path: '/habitos' })

    expect(await screen.findByText('Contenido privado')).toBeInTheDocument()
  })
})

describe('login', () => {
  const renderLogin = () =>
    renderWithProviders(
      <RedirectIfAuthenticated>
        <LoginPage />
      </RedirectIfAuthenticated>,
      {
        route: '/entrar',
        path: '/entrar',
        signedIn: false,
        extraRoutes: [{ path: '/', element: <p>Panel</p> }],
      },
    )

  it('validates the form before calling the API', async () => {
    const user = userEvent.setup()
    renderLogin()

    await user.click(await screen.findByRole('button', { name: 'Entrar' }))

    expect(await screen.findByText('Introduce un correo electrónico válido.')).toBeInTheDocument()
    expect(screen.getByText('Introduce tu contraseña.')).toBeInTheDocument()
    expect(screen.getByLabelText(/Correo electrónico/)).toHaveAttribute('aria-invalid', 'true')
  })

  it('shows the generic API error for wrong credentials', async () => {
    server.use(http.post(`${API}/auth/login`, () => problem(401, 'Credenciales no válidas.', 'unauthorized')))
    const user = userEvent.setup()
    renderLogin()

    await user.type(await screen.findByLabelText(/Correo electrónico/), 'ana@test.local')
    await user.type(screen.getByLabelText(/^Contraseña/), 'Wrong123')
    await user.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Credenciales no válidas.')
  })

  it('signs in with the anti-forgery header and goes to the dashboard', async () => {
    let csrf: string | null = null
    let body: unknown
    server.use(
      http.post(`${API}/auth/login`, async ({ request }) => {
        csrf = request.headers.get('X-XSRF-TOKEN')
        body = await request.json()
        return HttpResponse.json(testUser)
      }),
    )
    const user = userEvent.setup()
    renderLogin()

    await user.type(await screen.findByLabelText(/Correo electrónico/), 'ana@test.local')
    await user.type(screen.getByLabelText(/^Contraseña/), 'Secret123')
    await user.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(await screen.findByText('Panel')).toBeInTheDocument()
    expect(csrf).toBe('test-csrf')
    expect(body).toEqual({ email: 'ana@test.local', password: 'Secret123', rememberMe: true })
  })

  it('offers password recovery only when the server supports it', async () => {
    renderLogin()
    expect(await screen.findByRole('link', { name: '¿La olvidaste?' })).toBeInTheDocument()
  })
})

describe('registration', () => {
  it('checks the password policy and confirmation', async () => {
    const user = userEvent.setup()
    renderWithProviders(<RegisterPage />, { route: '/registro', path: '/registro', signedIn: false })

    await user.type(await screen.findByLabelText(/^Nombre/), 'Ana')
    await user.type(screen.getByLabelText(/Correo electrónico/), 'ana@test.local')
    await user.type(screen.getByLabelText(/^Contraseña/), 'weak')
    await user.type(screen.getByLabelText(/Repite la contraseña/), 'other')
    await user.click(screen.getByRole('button', { name: 'Crear cuenta' }))

    expect(await screen.findByText('Mínimo 8 caracteres.')).toBeInTheDocument()
    expect(screen.getByText('Las contraseñas no coinciden.')).toBeInTheDocument()
  })
})
