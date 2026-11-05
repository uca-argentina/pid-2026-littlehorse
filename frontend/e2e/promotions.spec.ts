import { devices, expect, test } from '@playwright/test';
import type { APIRequestContext } from '@playwright/test';
import { categoryIdNamed } from './categories';
import { evidence } from './evidence';
import { aFullName, aKdsAccount, logIn } from './kds';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';
import { loadIntoTonight } from './stock';

/**
 * US-54 to US-56, end to end: the administrator sets up a happy hour on a
 * computer, a customer sees it on the menu from a phone and pays the discounted
 * price, and the tracking screen says how much it saved.
 */
const promotionsPath = `/${seededVenueSlug}/staff/promotions`;
const menuPath = `/${seededVenueSlug}/menu`;

const unique = () => Date.now().toString(36) + Math.random().toString(36).slice(2, 5);

const WEEK = ['Lunes', 'Martes', 'Miércoles', 'Jueves', 'Viernes', 'Sábado', 'Domingo'];

/** "HH:mm" at the venue, `offsetHours` from now: the promotion's hours are Buenos Aires hours. */
function venueClock(offsetHours: number): string {
  const moment = new Date(Date.now() + offsetHours * 3_600_000);

  return new Intl.DateTimeFormat('en-GB', {
    timeZone: 'America/Argentina/Buenos_Aires',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).format(moment);
}

async function adminToken(request: APIRequestContext): Promise<string> {
  const login = await request.post(`/api/${seededVenueSlug}/auth/login`, {
    data: { username: seededAdminUsername, password: seededAdminPassword() },
  });

  return ((await login.json()) as { token: string }).token;
}

async function aProductIn(
  request: APIRequestContext,
  token: string,
  name: string,
  kds: string,
): Promise<void> {
  const created = await request.post('/api/staff/products', {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      name,
      price: 4000,
      stock: 10,
      categoryId: await categoryIdNamed(request, token, 'Tragos'),
    },
  });

  expect(created.status()).toBe(201);

  const { id } = (await created.json()) as { id: string };

  await loadIntoTonight(request, token, id, 10, kds);
}

test.describe('Promotions', () => {
  test('are set up on a computer, shown on the menu, charged and counted as savings', async ({
    browser,
    request,
  }) => {
    const token = await adminToken(request);
    const kds = await aKdsAccount(request, `Barra ${unique()}`);
    const productName = `Gin ${unique()}`;
    const promotionName = `Happy hour ${unique()}`;

    await aProductIn(request, token, productName, kds);

    // The administrator, on a computer.
    const desk = await browser.newPage({ viewport: { width: 1440, height: 900 } });
    await logIn(desk, seededAdminUsername, seededAdminPassword());
    // Through the header, not by address: a reload would drop the session.
    await desk.getByRole('link', { name: 'Promociones' }).click();
    await desk.getByRole('link', { name: 'Nueva promoción' }).click();

    await desk.getByLabel('Nombre', { exact: true }).fill(promotionName);
    await desk.getByLabel('Descuento (%)').fill('30');

    for (const day of WEEK) await desk.getByLabel(day, { exact: true }).check();

    // Around now, whatever time the suite runs at, midnight included.
    await desk.getByLabel('Desde').fill(venueClock(-1));
    await desk.getByLabel('Hasta').fill(venueClock(1));
    await desk.getByLabel('Buscar producto').fill(productName);
    await desk.getByLabel(productName, { exact: true }).check();
    await evidence(desk, '06-promocion-formulario');

    await desk.getByRole('button', { name: 'Guardar promoción' }).click();
    await expect(desk).toHaveURL(new RegExp(`${promotionsPath}$`));

    const row = desk.getByRole('row', { name: new RegExp(promotionName) });

    await expect(row).toContainText('30%');
    await expect(row).toContainText('Todos los días');
    await expect(row).toContainText(productName);
    await expect(row).toContainText('Activa');
    await evidence(desk, '05-promocion-listado');
    await desk.close();

    // The customer, on a phone.
    const phoneContext = await browser.newContext({ ...devices['iPhone 13'] });
    const phone = await phoneContext.newPage();
    await phone.goto(menuPath);
    await phone.getByRole('searchbox', { name: /buscar trago/i }).fill(productName);
    await expect(phone.getByText('$ 2.800,00')).toBeVisible();
    await expect(phone.locator('s', { hasText: '$ 4.000,00' })).toBeVisible();
    await expect(phone.getByText(promotionName)).toBeVisible();
    await evidence(phone, '07-promocion-en-la-carta');

    await phone.getByRole('button', { name: new RegExp(`agregar ${productName}`, 'i') }).click();
    await phone.getByTestId('order-summary').click();
    await phone.getByRole('link', { name: /ir a pagar/i }).click();
    await expect(phone.getByTestId('total')).toContainText('2.800,00');
    await phone.getByRole('textbox', { name: /nombre/i }).fill(aFullName());
    await phone.getByRole('button', { name: /pagar/i }).click();

    await expect(phone.getByTestId('order-code')).toBeVisible({ timeout: 15000 });
    await expect(phone.getByTestId('savings')).toContainText('Te ahorraste $ 1.200,00');
    await evidence(phone, '08-promocion-ahorro');
    await phoneContext.close();
  });
});
