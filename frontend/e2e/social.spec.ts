import { expect, test } from '@playwright/test'
import { createHabit, registerNewUser } from './helpers'

test('two people share a goal and a challenge without seeing each other’s habits', async ({ browser }) => {
  const ownerContext = await browser.newContext({ locale: 'es-ES', timezoneId: 'Europe/Madrid' })
  const guestContext = await browser.newContext({ locale: 'es-ES', timezoneId: 'Europe/Madrid' })
  const owner = await ownerContext.newPage()
  const guest = await guestContext.newPage()

  await registerNewUser(owner, 'Marta')
  const guestEmail = await registerNewUser(guest, 'Iván')
  await createHabit(owner, { name: 'Lectura secreta' })
  await createHabit(guest, { name: 'Correr' })

  // Shared goal: invite by e-mail, accept, contribute.
  await owner.goto('/compartidos')
  await owner.getByRole('button', { name: 'Nuevo objetivo compartido' }).click()
  const form = owner.getByRole('dialog')
  await form.getByLabel(/^Nombre/).fill('Leer 20 libros juntos')
  await form.getByLabel(/^Unidad/).fill('libros')
  await form.getByLabel(/^Meta colectiva/).fill('20')
  await form.getByRole('button', { name: 'Guardar' }).click()
  await expect(owner.getByRole('heading', { name: 'Leer 20 libros juntos' })).toBeVisible()
  await owner.getByLabel('Correo de la persona invitada').fill(guestEmail)
  await owner.getByRole('button', { name: 'Invitar' }).click()
  await expect(owner.getByText(/Invitación registrada/)).toBeVisible()

  await guest.goto('/compartidos')
  await expect(guest.getByText(/Marta.*te invita a «Leer 20 libros juntos»/)).toBeVisible()
  await guest.getByRole('button', { name: 'Aceptar' }).click()
  await expect(guest.getByRole('heading', { name: 'Leer 20 libros juntos' })).toBeVisible()
  await guest.getByLabel(/Cantidad/).fill('3')
  await guest.getByLabel(/Nota privada/).fill('solo mía')
  await guest.getByRole('button', { name: 'Añadir' }).click()
  await expect(guest.getByText('Aportación registrada')).toBeVisible()

  await owner.reload()
  await expect(owner.getByRole('progressbar', { name: 'Progreso colectivo' })).toHaveAttribute(
    'aria-valuenow',
    '15',
  )
  await expect(owner.getByText('solo mía')).toHaveCount(0)

  // Challenge: join with a code using one's own habit.
  await owner.goto('/retos')
  await owner.getByRole('button', { name: 'Crear reto' }).click()
  const challengeForm = owner.getByRole('dialog')
  await challengeForm.getByLabel(/^Nombre/).fill('Semana constante')
  await challengeForm.getByLabel(/^Meta/).fill('3')
  await challengeForm.getByLabel('Participar con mi hábito').selectOption({ label: 'Lectura secreta' })
  await challengeForm.getByRole('button', { name: 'Crear reto' }).click()
  await expect(owner.getByRole('heading', { name: 'Semana constante' })).toBeVisible()
  const code = (await owner.locator('code').innerText()).trim()

  await guest.goto('/retos')
  await guest.getByRole('button', { name: 'Unirme con un código' }).click()
  const join = guest.getByRole('dialog')
  await join.getByLabel('Código del reto').fill(code)
  await expect(join.getByText('Semana constante')).toBeVisible()
  await join.getByLabel('¿Con qué hábito participas?').selectOption({ label: 'Correr' })
  await join.getByRole('button', { name: 'Unirme' }).click()
  await expect(guest.getByRole('heading', { name: 'Clasificación' })).toBeVisible()
  await expect(guest.getByText('Marta')).toBeVisible()
  await expect(guest.getByText('Lectura secreta')).toHaveCount(0)

  await ownerContext.close()
  await guestContext.close()
})
