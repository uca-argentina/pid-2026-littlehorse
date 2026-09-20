import { expect, test } from '@playwright/test';
import type { APIRequestContext, Page } from '@playwright/test';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';

/**
 * US-11, end to end: somebody pays for the order they put together and walks
 * away with a code. This is the first customer journey that writes to the
 * database, so what it proves is the whole chain — the phone's order becoming
 * an order of the venue, priced from the venue's own menu.
 */
const menuPath = `/${seededVenueSlug}/menu`;

/** Unique per run: the development database keeps everything every run created. */
function aNewProduct(): string {
  return `E2E ${Date.now().toString(36)}${Math.random().toString(36).slice(2, 5)}`;
}

async function loadProduct(request: APIRequestContext, name: string, stock: number): Promise<void> {
  const login = await request.post(`/api/${seededVenueSlug}/auth/login`, {
    data: { username: seededAdminUsername, password: seededAdminPassword() },
  });
  const { token } = (await login.json()) as { token: string };

  const created = await request.post('/api/staff/products', {
    headers: { Authorization: `Bearer ${token}` },
    data: { name, description: 'Cargado por la prueba', price: 4500, stock },
  });

  expect(created.status()).toBe(201);
}

/** Puts one drink in the order and opens the payment screen. */
async function anOrderReadyToPay(page: Page, request: APIRequestContext, stock = 20) {
  const name = aNewProduct();
  await loadProduct(request, name, stock);

  await page.goto(menuPath);
  await page.getByRole('searchbox', { name: /buscar trago/i }).fill(name);
  await page.getByRole('button', { name: new RegExp(`agregar ${name}`, 'i') }).click();
  await page.getByTestId('order-summary').click();
  await page.getByRole('link', { name: /ir a pagar/i }).click();

  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Pagar');

  return name;
}

test.describe('Checkout', () => {
  /**
   * The whole journey, in one test, because the point of it is that the pieces
   * join up: the QR, the menu, the order, the payment and the code.
   */
  test('walks from the QR to a confirmed order with its code', async ({ page, request }) => {
    const name = await anOrderReadyToPay(page, request);

    await expect(page.getByTestId('total')).toContainText('$ 4.500,00');
    await expect(page.getByText(name)).toBeVisible();

    await page.getByRole('textbox', { name: /nombre/i }).fill('María Quadro');
    await page.getByRole('button', { name: /pagar/i }).click();

    // The code the bar will ask for. Its shape is the contract: one letter, a
    // hyphen and four digits.
    await expect(page.getByTestId('order-code')).toHaveText(/^[A-Z]-\d{4}$/, { timeout: 15000 });
    await expect(page).toHaveURL(
      new RegExp(`/${seededVenueSlug}/orders/[A-Z]-\\d{4}/[0-9a-f]{32}$`),
    );
    await expect(page.getByRole('status')).toContainText(/pago/i);
  });

  // Criterion 3, in a real browser: no name, no order, and it is said before
  // anything is sent rather than after.
  test('does not let the order be paid for without a full name', async ({ page, request }) => {
    await anOrderReadyToPay(page, request);

    await expect(page.getByRole('button', { name: /pagar/i })).toBeDisabled();

    await page.getByRole('textbox', { name: /nombre/i }).fill('Euge');
    await expect(page.getByRole('button', { name: /pagar/i })).toBeDisabled();

    await page.getByRole('textbox', { name: /nombre/i }).fill('Euge Quadro');
    await expect(page.getByRole('button', { name: /pagar/i })).toBeEnabled();
  });

  // The order belongs to the server now, so the phone lets go of its copy: the
  // bar at the bottom of the menu is gone and a new night starts empty.
  test('leaves the phone with nothing in it once the order is paid', async ({ page, request }) => {
    await anOrderReadyToPay(page, request);

    await page.getByRole('textbox', { name: /nombre/i }).fill('María Quadro');
    await page.getByRole('button', { name: /pagar/i }).click();
    await expect(page.getByTestId('order-code')).toBeVisible({ timeout: 15000 });

    await page.goto(menuPath);

    await expect(page.getByTestId('order-summary')).toHaveCount(0);
  });

  // The code is in the address, so locking the phone and coming back still
  // shows what the bar is going to ask for.
  test('still shows the code after a reload', async ({ page, request }) => {
    await anOrderReadyToPay(page, request);

    await page.getByRole('textbox', { name: /nombre/i }).fill('María Quadro');
    await page.getByRole('button', { name: /pagar/i }).click();
    await expect(page.getByTestId('order-code')).toBeVisible({ timeout: 15000 });

    const code = await page.getByTestId('order-code').textContent();
    await page.reload();

    await expect(page.getByTestId('order-code')).toHaveText(code!);
  });

  // Two drinks asked for, one left on the shelf. Nobody pays for an order they
  // will not get in full, and the screen says which drink.
  test('refuses the order when a drink ran out, and names it', async ({ page, request }) => {
    const name = await anOrderReadyToPay(page, request, 1);

    // A second one of the same drink, which the venue cannot serve. The way
    // back to the carta is through the order screen, not from here.
    await page.getByRole('link', { name: /volver al pedido/i }).click();
    await page.getByRole('link', { name: /agregar más tragos/i }).click();
    await page.getByRole('searchbox', { name: /buscar trago/i }).fill(name);
    await page.getByRole('button', { name: new RegExp(`agregar ${name}`, 'i') }).click();
    await page.getByTestId('order-summary').click();
    await page.getByRole('link', { name: /ir a pagar/i }).click();

    await page.getByRole('textbox', { name: /nombre/i }).fill('María Quadro');
    await page.getByRole('button', { name: /pagar/i }).click();

    await expect(page.getByRole('alert')).toContainText(name, { timeout: 15000 });
    await expect(page.getByRole('link', { name: /revisar el pedido/i })).toBeVisible();
  });

  // US-12: paying lands on the order's own screen, which is also where it is
  // followed from. The link carries the token, because that is the only thing
  // that opens it.
  test('lands on the screen that follows the order', async ({ page, request }) => {
    await anOrderReadyToPay(page, request);

    await page.getByRole('textbox', { name: /nombre/i }).fill('María Quadro');
    await page.getByRole('button', { name: /pagar/i }).click();
    await expect(page.getByTestId('order-code')).toBeVisible({ timeout: 15000 });

    await expect(page.getByRole('listitem').first()).toContainText('Esperando en la barra');
    await expect(page).toHaveURL(/\/orders\/[A-Z]-\d{4}\/[0-9a-f]{32}$/);
  });
});
