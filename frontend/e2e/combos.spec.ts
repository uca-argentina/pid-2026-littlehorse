import { devices, expect, test } from '@playwright/test';
import type { APIRequestContext } from '@playwright/test';
import { categoryIdNamed } from './categories';
import { evidence } from './evidence';
import { aFullName, aKdsAccount, aNewPassword, logIn } from './kds';
import { tonightsNight } from './nights';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';
import { loadIntoTonight } from './stock';

/**
 * US-50 to US-53, end to end: the administrator arms a combo on a computer, a
 * customer orders it from a phone, the bar reads what it brings, and each
 * component comes off that bar's stock.
 */
const productsPath = `/${seededVenueSlug}/staff/products`;
const menuPath = `/${seededVenueSlug}/menu`;
const kdsPath = `/${seededVenueSlug}/staff/kds`;

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

test.describe('Combos', () => {
  test('are armed on a computer, ordered from a phone and taken apart at the bar', async ({
    browser,
    request,
  }) => {
    const token = await adminToken(request);
    const kds = await aKdsAccount(request, `Barra ${unique()}`);
    const fernetName = `Fernet ${unique()}`;
    const iceName = `Hielo ${unique()}`;
    const comboName = `Combo ${unique()}`;
    const fernet = await aProductIn(request, token, fernetName, kds);
    const ice = await aProductIn(request, token, iceName, kds);

    // The administrator, on a computer.
    const desk = await browser.newPage({ viewport: { width: 1440, height: 900 } });
    await logIn(desk, seededAdminUsername, seededAdminPassword());
    await expect(desk).toHaveURL(new RegExp(`${productsPath}$`));
    await desk.getByRole('link', { name: 'Nuevo combo' }).click();

    await desk.getByLabel('Nombre').fill(comboName);
    await desk.getByLabel('Precio del combo').fill('7000');
    await desk.getByLabel('Categoría').selectOption({ label: 'Tragos' });
    await desk.getByLabel('Producto', { exact: true }).selectOption({ label: fernetName });
    await desk.getByLabel('Cantidad', { exact: true }).fill('2');
    await desk.getByRole('button', { name: 'Agregar' }).click();
    await desk.getByLabel('Producto', { exact: true }).selectOption({ label: iceName });
    await desk.getByRole('button', { name: 'Agregar' }).click();
    await expect(desk.getByRole('table').getByText(iceName)).toBeVisible();
    await expect(desk.getByText('Por separado cuesta $ 12.000,00')).toBeVisible();
    await evidence(desk, '01-combo-admin-escritorio');

    await desk.getByRole('button', { name: 'Guardar combo' }).click();
    await expect(desk).toHaveURL(new RegExp(`${productsPath}$`));
    await desk.getByRole('searchbox', { name: /buscar producto/i }).fill(comboName);
    const row = desk.getByRole('listitem', { name: comboName });
    await expect(row).toContainText(`Trae 2 × ${fernetName}, 1 × ${iceName}`);
    await evidence(desk, '02-combo-en-listado');
    await desk.close();

    // The customer, on a phone.
    const phoneContext = await browser.newContext({ ...devices['iPhone 13'] });
    const phone = await phoneContext.newPage();
    await phone.goto(menuPath);
    await phone.getByRole('searchbox', { name: /buscar trago/i }).fill(comboName);
    await expect(phone.getByText(`Trae 2 × ${fernetName}, 1 × ${iceName}`)).toBeVisible();
    await evidence(phone, '03-combo-en-la-carta');
    await phone.getByRole('button', { name: new RegExp(`agregar ${comboName}`, 'i') }).click();
    await phone.getByTestId('order-summary').click();
    await phone.getByRole('link', { name: /ir a pagar/i }).click();
    await phone.getByRole('textbox', { name: /nombre/i }).fill(aFullName());
    await phone.getByRole('button', { name: /pagar/i }).click();
    await expect(phone.getByTestId('order-code')).toBeVisible({ timeout: 15000 });
    await phoneContext.close();

    // Each component came off, and the combo holds nothing.
    expect(await soldOf(request, token, fernet)).toBe(2);
    expect(await soldOf(request, token, ice)).toBe(1);

    // The bar, on its tablet.
    const tablet = await browser.newPage({ viewport: { width: 820, height: 1180 } });
    await logIn(tablet, kds, aNewPassword);
    await expect(tablet).toHaveURL(new RegExp(`${kdsPath}$`));
    const parts = tablet.getByRole('list', { name: `Qué trae ${comboName}` });
    await expect(parts).toContainText(`2× ${fernetName}`);
    await expect(parts).toContainText(`1× ${iceName}`);
    await evidence(tablet, '04-combo-en-el-kds');
    await tablet.close();
  });
});
