import { expect, test } from '@playwright/test'
import { createHabit, registerNewUser } from './helpers'

test('mobile: bottom navigation works and no screen scrolls sideways', async ({ page }) => {
  await registerNewUser(page, 'Lucía')
  await createHabit(page, { name: 'Caminar' })

  const nav = page.getByRole('navigation', { name: 'Navegación móvil' })
  await expect(nav).toBeVisible()
  await expect(page.getByRole('navigation', { name: 'Principal' })).toBeHidden()

  for (const [label, heading] of [
    ['Hoy', /, Lucía$/],
    ['Hábitos', 'Hábitos'],
    ['Calendario', 'Calendario'],
    ['Objetivos', 'Objetivos'],
    ['Estadísticas', 'Estadísticas'],
  ] as const) {
    await nav.getByRole('link', { name: label }).click()
    await expect(page.getByRole('heading', { level: 1, name: heading })).toBeVisible()
    await page.waitForLoadState('networkidle')
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    )
    expect(overflow, `horizontal overflow on ${label}`).toBeLessThanOrEqual(0)
  }

  await nav.getByRole('link', { name: 'Hoy' }).click()
  await page.getByRole('button', { name: 'Marcar «Caminar» como completado' }).click()
  await expect(page.getByText('1 de 1 hábitos de hoy completados')).toBeVisible()
})
