import { expect, test } from '@playwright/test';
import type { Page } from '@playwright/test';
import { aFullName, aKdsAccount, anOrderPaidBy, loginPath } from './kds';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';

/**
 * US-47 to US-49, end to end against the real API and the seeded database:
 * the administrator opens Reportes and lands on tonight's report, which counts
 * an order the moment it is paid. The development database keeps every run's
 * orders, so the checks compare before and after instead of expecting totals.
 */
const reportsPath = `/${seededVenueSlug}/staff/reports`;
const nightsPath = `/${seededVenueSlug}/staff/nights`;

async function logInAsTheAdministrator(page: Page): Promise<void> {
  await page.goto(loginPath);
  await page.getByRole('textbox', { name: /usuario/i }).fill(seededAdminUsername);
  await page.getByRole('textbox', { name: /contraseña/i }).fill(seededAdminPassword());
  await page.getByRole('button', { name: /entrar/i }).click();
  await expect(page).not.toHaveURL(new RegExp(`${loginPath}$`));
}

async function ordersOnScreen(page: Page): Promise<number> {
  const orders = page.getByTestId('orders');
  await expect(orders).toBeVisible();

  return Number((await orders.locator('.value').textContent())?.replace(/\D/g, ''));
}

test.describe('Reports', () => {
  test('opens on tonight, live, and counts an order once it is paid', async ({
    page,
    browser,
    request,
  }) => {
    const barName = `Barra ${Date.now().toString(36)}`;
    const bar = await aKdsAccount(request, barName);

    await logInAsTheAdministrator(page);
    await page.getByRole('link', { name: 'Reportes' }).click();

    // night.setup.ts left a night on: the tab goes straight to it.
    await expect(page).toHaveURL(new RegExp(`${reportsPath}/[0-9a-f-]{36}$`));
    await expect(page.getByText('En vivo')).toBeVisible();
    const before = await ordersOnScreen(page);

    const customer = await browser.newPage();
    await anOrderPaidBy(customer, request, aFullName(), bar);
    await customer.close();

    await page.reload();

    expect(await ordersOnScreen(page)).toBeGreaterThan(before);
    // US-47, criterion 2: every bar of the night has its row, the new one too.
    await expect(page.getByRole('table')).toContainText(barName);
  });

  test("is one tap away from a night's own screen", async ({ page }) => {
    await logInAsTheAdministrator(page);
    await page.goto(nightsPath);
    await page.getByRole('link').filter({ hasText: 'En curso' }).first().click();

    await page.getByRole('link', { name: /ver reporte de la noche/i }).click();

    await expect(page).toHaveURL(new RegExp(`${reportsPath}/[0-9a-f-]{36}$`));
    await expect(page.getByTestId('sold')).toBeVisible();
  });
});
