import { expect, test } from '@playwright/test';
import type { APIRequestContext, Page } from '@playwright/test';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';

/**
 * US-10, end to end: somebody builds an order on the menu, opens it, fixes it
 * and writes an aclaración. Nothing here touches the API — the order lives on
 * the device — so what this proves in a real browser is the journey and the
 * storage, which is exactly what a unit test cannot.
 */
const menuPath = `/${seededVenueSlug}/menu`;
const orderPath = `/${seededVenueSlug}/order`;

/** Unique per run: the development database keeps everything every run created. */
function aNewProduct(): string {
  return `E2E ${Date.now().toString(36)}${Math.random().toString(36).slice(2, 5)}`;
}

async function loadProduct(request: APIRequestContext, name: string): Promise<void> {
  const login = await request.post(`/api/${seededVenueSlug}/auth/login`, {
    data: { username: seededAdminUsername, password: seededAdminPassword() },
  });
  const { token } = (await login.json()) as { token: string };

  const created = await request.post('/api/staff/products', {
    headers: { Authorization: `Bearer ${token}` },
    data: { name, description: 'Cargado por la prueba', price: 4500, stock: 20 },
  });

  expect(created.status()).toBe(201);
}

/** Opens the menu with one drink already in the order, and only that drink on screen. */
async function anOrderWith(page: Page, request: APIRequestContext, howMany: number) {
  const name = aNewProduct();
  await loadProduct(request, name);

  await page.goto(menuPath);
  await page.getByRole('searchbox', { name: /buscar trago/i }).fill(name);

  const add = page.getByRole('button', { name: new RegExp(`agregar ${name}`, 'i') });

  for (let tap = 0; tap < howMany; tap += 1) await add.click();

  await expect(page.getByTestId(`quantity-${name}`)).toHaveText(String(howMany));

  return name;
}

test.describe('Order', () => {
  test('walks from the menu to the order and back', async ({ page, request }) => {
    const name = await anOrderWith(page, request, 2);

    await page.getByTestId('order-summary').click();

    await expect(page).toHaveURL(new RegExp(`${orderPath}$`));
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Tu pedido');
    await expect(page.getByRole('listitem').filter({ hasText: name })).toBeVisible();
    await expect(page.getByTestId(`quantity-${name}`)).toHaveText('2');
    await expect(page.getByTestId('total')).toContainText('$ 9.000,00');

    await page.getByRole('link', { name: 'Agregar más tragos' }).click();

    await expect(page).toHaveURL(new RegExp(`${menuPath}$`));
  });

  // US-10, criteria 2 and 3, on the screen that owns them.
  test('fixes the order from the order screen', async ({ page, request }) => {
    const name = await anOrderWith(page, request, 1);

    await page.goto(orderPath);

    await page.getByRole('button', { name: new RegExp(`agregar otro ${name}`, 'i') }).click();
    await expect(page.getByTestId(`quantity-${name}`)).toHaveText('2');
    await expect(page.getByTestId('total')).toContainText('$ 9.000,00');

    await page.getByRole('button', { name: new RegExp(`quitar un ${name}`, 'i') }).click();
    await expect(page.getByTestId(`quantity-${name}`)).toHaveText('1');

    await page.getByRole('button', { name: new RegExp(`sacar ${name} del pedido`, 'i') }).click();

    await expect(page.getByRole('listitem')).toHaveCount(0);
    await expect(page.getByRole('status')).toContainText(/todavía no agregaste nada/i);
    await expect(page.getByTestId('total')).toContainText('$ 0,00');
  });

  /**
   * US-10, criterion 5. The note is written on the menu card and read back on
   * the order screen: the two entry points are the same note, which is the
   * thing worth proving in a browser with real storage in between.
   */
  test('carries the note written on the card through to the order', async ({ page, request }) => {
    const name = await anOrderWith(page, request, 1);

    await page
      .getByRole('button', { name: new RegExp(`escribir una nota para ${name}`, 'i') })
      .click();
    await page.getByRole('textbox', { name: `Nota para ${name}` }).fill('sin hielo');
    await page
      .getByRole('button', { name: new RegExp(`listo con la nota de ${name}`, 'i') })
      .click();

    // Closed, the card shows what was written under the name of the drink.
    await expect(page.getByTestId(`note-${name}`)).toHaveText('sin hielo');

    await page.getByTestId('order-summary').click();
    await expect(page).toHaveURL(new RegExp(`${orderPath}$`));

    const note = page.getByRole('textbox', { name: `Nota para ${name}` });

    await expect(note).toHaveValue('sin hielo');

    // And edited from here, where somebody rereads their order before paying.
    await note.fill('con mucho limón');
    await page.reload();

    await expect(page.getByRole('textbox', { name: `Nota para ${name}` })).toHaveValue(
      'con mucho limón',
    );
  });

  // US-11 exists now, so the gold bar is the way on to paying.
  test('leads to the payment screen', async ({ page, request }) => {
    await anOrderWith(page, request, 1);

    await page.goto(orderPath);
    await page.getByRole('link', { name: /ir a pagar/i }).click();

    await expect(page).toHaveURL(new RegExp(`/${seededVenueSlug}/checkout$`));
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Pagar');
  });
});
