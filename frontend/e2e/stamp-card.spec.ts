import { devices, expect, test } from '@playwright/test';
import type { APIRequestContext, Page } from '@playwright/test';
import { categoryIdNamed } from './categories';
import { evidence } from './evidence';
import { aFullName, aKdsAccount, aNewPassword, logIn } from './kds';
import { tonightsNight } from './nights';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';
import { loadIntoTonight } from './stock';

/**
 * US-57 to US-59, end to end: the administrator sets up the stamp card on a
 * computer, a customer orders from a phone, the bartender hands it over, the
 * stamps show on the menu, and the customer takes their reward at zero, which
 * comes off the bar's stock like any other drink.
 */
const menuPath = `/${seededVenueSlug}/menu`;

const unique = () => Date.now().toString(36) + Math.random().toString(36).slice(2, 5);

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
): Promise<string> {
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

  return id;
}

interface SheetLine {
  readonly productId: string;
  readonly sold: number;
}

async function soldOf(
  request: APIRequestContext,
  token: string,
  productId: string,
): Promise<number> {
  const headers = { Authorization: `Bearer ${token}` };
  const night = await tonightsNight(request, headers);
  const sheet = (await (
    await request.get(`/api/nights/${night.id}/stock`, { headers })
  ).json()) as {
    bars: { lines: SheetLine[] }[];
  };

  return sheet.bars
    .flatMap((bar) => bar.lines)
    .filter((line) => line.productId === productId)
    .reduce((sum, line) => sum + line.sold, 0);
}

/** Adds drinks from the menu, goes to the checkout and fills in a name. */
async function startPaying(phone: Page, drink: string, units: number): Promise<string> {
  const name = aFullName();

  await phone.goto(menuPath);
  await phone.getByRole('searchbox', { name: /buscar trago/i }).fill(drink);
  for (let unit = 0; unit < units; unit++)
    await phone.getByRole('button', { name: new RegExp(`agregar ${drink}`, 'i') }).click();
  await phone.getByTestId('order-summary').click();
  await phone.getByRole('link', { name: /ir a pagar/i }).click();
  await phone.getByRole('textbox', { name: /nombre/i }).fill(name);

  return name;
}

test.describe('Stamp card', () => {
  test('is set up on a computer, earned with delivered drinks and redeemed at zero', async ({
    browser,
    request,
  }) => {
    const token = await adminToken(request);
    const kds = await aKdsAccount(request, `Barra ${unique()}`);
    const rewardName = `Fernet ${unique()}`;
    const paidName = `Gin ${unique()}`;
    const reward = await aProductIn(request, token, rewardName, kds);
    const paid = await aProductIn(request, token, paidName, kds);

    // The administrator, on a computer.
    const desk = await browser.newPage({ viewport: { width: 1440, height: 900 } });
    await logIn(desk, seededAdminUsername, seededAdminPassword());
    // Through the header, not by address: a reload would drop the session.
    await desk.getByRole('link', { name: 'Tarjeta' }).click();
    // Retried: the rule a previous run saved can land in the form mid-typing.
    await expect(async () => {
      await desk.getByLabel('Sellos para el regalo').fill('2');
      await desk.getByLabel('Producto de regalo').selectOption({ label: rewardName });
      await desk.getByLabel('Tarjeta activa').check();
      await expect(desk.getByTestId('stamps-progress')).toContainText('0 de 2 sellos', {
        timeout: 1000,
      });
    }).toPass();
    await evidence(desk, '09-tarjeta-admin-escritorio');

    await desk.getByRole('button', { name: 'Guardar tarjeta' }).click();
    await expect(desk.getByRole('status')).toContainText('Guardamos la tarjeta');
    await desk.close();

    // The customer, on a phone: the first order gives the phone its card.
    const phoneContext = await browser.newContext({ ...devices['iPhone 13'] });
    const phone = await phoneContext.newPage();
    const firstName = await startPaying(phone, paidName, 2);

    await phone.getByRole('button', { name: /pagar/i }).click();
    await expect(phone.getByTestId('order-code')).toBeVisible({ timeout: 15000 });

    const firstCode = (await phone.getByTestId('order-code').textContent())!.trim();

    // Not delivered yet: a drink earns its stamp when it is handed over.
    await phone.goto(menuPath);
    await expect(phone.getByTestId('stamps-progress')).toContainText('0 de 2 sellos');

    // The bartender, on the bar's tablet.
    const tablet = await browser.newPage({ viewport: { width: 820, height: 1180 } });
    await logIn(tablet, kds, aNewPassword);

    const column = (group: string, customer: string) =>
      tablet.getByRole('group', { name: group }).getByRole('article').filter({ hasText: customer });

    await column('Nuevos', firstName)
      .getByRole('button', { name: /^Preparar / })
      .click();
    await column('En preparación', firstName)
      .getByRole('button', { name: /^Listo / })
      .click();
    await column('Listos en la barra', firstName)
      .getByRole('button', { name: /^Entregado / })
      .click();
    await expect(column('Listos en la barra', firstName)).toHaveCount(0);

    // Two drinks handed over, two stamps, on the menu.
    await phone.goto(menuPath);
    await expect(phone.getByTestId('stamps-progress')).toContainText('2 de 2 sellos');
    await expect(phone.getByTestId('stamps-ready')).toContainText(rewardName);
    // Narrowed so the picture shows the card and not every product of the shared venue.
    await phone.getByRole('searchbox', { name: /buscar trago/i }).fill(paidName);
    await evidence(phone, '10-tarjeta-progreso-en-la-carta');

    // The next order brings the reward, shown at zero.
    await startPaying(phone, paidName, 1);
    await expect(phone.getByTestId('reward-offer')).toContainText(rewardName);
    await phone.getByRole('button', { name: /agregar mi regalo/i }).click();
    await expect(phone.getByTestId('reward-line')).toContainText('$ 0,00');
    await expect(phone.getByTestId('total')).toContainText('4.000,00');
    await evidence(phone, '11-tarjeta-regalo-en-checkout');

    await phone.getByRole('button', { name: /pagar/i }).click();
    await expect(phone.getByTestId('order-code')).toBeVisible({ timeout: 15000 });
    await expect(phone.getByTestId('reward-item')).toContainText(rewardName);
    await expect(phone.getByTestId('reward-item')).toContainText('Regalo');
    await expect(phone.getByTestId('stamps-progress')).toContainText('0 de 2 sellos');
    await evidence(phone, '12-tarjeta-regalo-en-seguimiento');
    await phoneContext.close();

    // The reward came off the bar like any drink, and the paid ones as usual.
    expect(await soldOf(request, token, reward)).toBe(1);
    expect(await soldOf(request, token, paid)).toBe(3);
    expect(firstCode).not.toBe('');
    await tablet.close();
  });
});
