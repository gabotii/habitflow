import { expect, type Page } from '@playwright/test'

export const PASSWORD = 'E2eSecret1'

export async function registerNewUser(page: Page, name = 'Elena') {
  const email = `e2e-${Date.now()}-${Math.random().toString(36).slice(2, 8)}@test.local`
  await page.goto('/registro')
  await page.getByLabel(/^Nombre/).fill(name)
  await page.getByLabel(/Correo electrónico/).fill(email)
  await page.getByLabel(/^Contraseña/).fill(PASSWORD)
  await page.getByLabel(/Repite la contraseña/).fill(PASSWORD)
  await page.getByLabel(/Zona horaria/).selectOption('Europe/Madrid')
  await page.getByRole('button', { name: 'Crear cuenta' }).click()
  await expect(page.getByRole('heading', { name: new RegExp(`, ${name}$`) })).toBeVisible()
  return email
}

/** Creates a habit from the habits page and lands on its detail page. */
export async function createHabit(
  page: Page,
  options: { name: string; quantity?: { unit: string; target: string }; startDaysAgo?: number },
) {
  await page.goto('/habitos')
  await page.getByRole('button', { name: 'Nuevo hábito' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel(/^Nombre/).fill(options.name)
  if (options.quantity) {
    await dialog.getByLabel('Tipo').selectOption('quantity')
    await dialog.getByLabel(/^Unidad/).fill(options.quantity.unit)
    await dialog.getByLabel('Meta diaria').fill(options.quantity.target)
  }
  if (options.startDaysAgo) {
    const start = new Date()
    start.setDate(start.getDate() - options.startDaysAgo)
    const value = `${start.getFullYear()}-${String(start.getMonth() + 1).padStart(2, '0')}-${String(start.getDate()).padStart(2, '0')}`
    await dialog.getByLabel('Fecha de inicio', { exact: true }).fill(value)
  }
  await dialog.getByRole('button', { name: 'Crear hábito' }).click()
  await expect(page.getByRole('heading', { level: 1, name: options.name })).toBeVisible()
}
