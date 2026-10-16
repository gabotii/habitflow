import { expect, test } from '@playwright/test'
import { createHabit, PASSWORD, registerNewUser } from './helpers'

test('register, create a habit, complete it and see it everywhere', async ({ page }) => {
  const email = await registerNewUser(page)
  await expect(page.getByText('Te damos la bienvenida a HabitFlow')).toBeVisible()

  await createHabit(page, { name: 'Meditar' })
  await page.getByRole('button', { name: 'Marcar «Meditar» como completado' }).click()
  await expect(page.getByRole('button', { name: 'Desmarcar «Meditar»' })).toBeVisible()
  await expect(page.getByTitle('Racha actual').first()).toHaveText(/1 día/)

  // Dashboard: real data from the API.
  await page.getByRole('navigation', { name: 'Principal' }).getByRole('link', { name: 'Hoy' }).click()
  await expect(page.getByText('1 de 1 hábitos de hoy completados')).toBeVisible()

  // Calendar: today is complete.
  await page.goto('/calendario')
  await expect(page.getByRole('button', { name: /: Todo cumplido, 1 de 1$/ })).toBeVisible()

  // Goal linked to the habit: progress is computed by the server from the log.
  await page.goto('/objetivos')
  await page.getByRole('button', { name: 'Nuevo objetivo' }).click()
  const goalDialog = page.getByRole('dialog')
  await goalDialog.getByLabel(/^Nombre/).fill('Meditar 20 veces')
  await goalDialog.getByText('Veces que cumples hábitos').click()
  await goalDialog.getByLabel('Meditar').check()
  await goalDialog.getByLabel(/^Meta/).fill('20')
  await goalDialog.getByRole('button', { name: 'Crear objetivo' }).click()
  await expect(page.getByRole('heading', { name: 'Meditar 20 veces' })).toBeVisible()
  await expect(page.getByRole('progressbar', { name: 'Progreso de Meditar 20 veces' })).toHaveAttribute(
    'aria-valuenow',
    '5',
  )

  // Reminder.
  await page.goto('/recordatorios')
  await page.getByRole('button', { name: 'Nuevo recordatorio' }).first().click()
  const reminderDialog = page.getByRole('dialog')
  await reminderDialog.getByLabel(/^Título/).fill('Hora de meditar')
  await reminderDialog.getByLabel(/^Hora/).fill('07:30')
  await reminderDialog.getByLabel('Vinculado a').selectOption({ label: 'Meditar' })
  await reminderDialog.getByRole('button', { name: 'Guardar' }).click()
  await expect(page.getByText('Hora de meditar')).toBeVisible()
  await expect(page.getByText(/Próximo:/)).toBeVisible()

  // Session: logout and login again.
  await page.getByRole('button', { name: 'Cerrar sesión' }).click()
  await expect(page).toHaveURL(/\/login$/)
  await page.goto('/habitos')
  await expect(page).toHaveURL(/\/login$/)
  await page.getByLabel('Correo electrónico').fill(email)
  await page.getByLabel('Contraseña').fill(PASSWORD)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page).toHaveURL(/\/habitos$/)
  await expect(page.getByRole('link', { name: 'Meditar' })).toBeVisible()
})

test('quantity habit: daily total, past corrections, archive and restore', async ({ page }) => {
  await registerNewUser(page, 'Pablo')
  await createHabit(page, { name: 'Leer', quantity: { unit: 'páginas', target: '10' }, startDaysAgo: 3 })

  // Today: exact amount through the dialog.
  await page.getByRole('button', { name: /Editar la cantidad de «Leer»/ }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel(/Cantidad del día/).fill('12')
  await dialog.getByRole('button', { name: 'Guardar' }).click()
  await expect(page.getByText('Registro guardado')).toBeVisible()
  await expect(page.locator('main').getByText('Completado').first()).toBeVisible()

  // Yesterday: correction from the weekly calendar (only possible inside the habit's window).
  // Open the ISO week that contains yesterday (on a Monday that is the previous week).
  const d = new Date()
  d.setDate(d.getDate() - 1)
  const iso = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
  await page.goto(`/calendario?vista=semana&fecha=${iso}`)
  const yesterday = page.getByRole('button', { name: /^Leer, .*: No cumplido$/ }).last()
  await yesterday.click()
  const correction = page.getByRole('dialog')
  await correction.getByLabel(/Cantidad del día/).fill('5')
  await correction.getByRole('button', { name: 'Guardar' }).click()
  await expect(page.getByRole('button', { name: /^Leer, .*: Parcial, 5 páginas$/ })).toBeVisible()

  // Archive keeps history; restore brings it back.
  await page.goto('/habitos')
  await page.getByRole('link', { name: 'Leer' }).click()
  await page.getByRole('button', { name: 'Archivar' }).click()
  await page.getByRole('alertdialog').getByRole('button', { name: 'Archivar' }).click()
  await expect(page.getByText('Hábito archivado. Su historial se conserva.')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Registros recientes' })).toBeVisible()
  await expect(page.getByText('12 páginas', { exact: true })).toBeVisible()
  await expect(page.getByText('Registrado: 12 páginas')).toBeVisible()
  await page.getByRole('button', { name: 'Restaurar' }).click()
  await expect(page.getByText('Hábito restaurado')).toBeVisible()
})
