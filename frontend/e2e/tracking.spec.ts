import { expect, test } from '@playwright/test';
import type { APIRequestContext, Page } from '@playwright/test';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';

/**
 * US-12, end to end: somebody watches their order from their table instead of
 * standing at the bar. What is worth running in a real browser is the link —
 * that it works later, and that it does not work for anybody else.
 */
const menuPath = `/${seededVenueSlug}/menu`;

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
    data: { name, description: 'Cargado por la prueba', price: 4500, stock: 20, category: 'Drink' },
  });

  expect(created.status()).toBe(201);
}

/** Buys one drink and ends up on the order's own screen. */
async function anOrderJustPaid(page: Page, request: APIRequestContext) {
  const name = aNewProduct();
  await loadProduct(request, name);

  await page.goto(menuPath);
  await page.getByRole('searchbox', { name: /buscar trago/i }).fill(name);
  await page.getByRole('button', { name: new RegExp(`agregar ${name}`, 'i') }).click();
  await page.getByTestId('order-summary').click();
  await page.getByRole('link', { name: /ir a pagar/i }).click();
  await page.getByRole('textbox', { name: /nombre/i }).fill('María Quadro');
  await page.getByRole('button', { name: /pagar/i }).click();

  await expect(page.getByTestId('order-code')).toBeVisible({ timeout: 15000 });

  return page.url();
}

test.describe('Tracking', () => {
  // Criteria 1 and 2: the number, and where it is in the journey.
  test('shows the code and the four steps of the journey', async ({ page, request }) => {
    await anOrderJustPaid(page, request);

    await expect(page.getByTestId('order-code')).toHaveText(/^[A-Z]-\d{4}$/);
    await expect(page.getByRole('listitem')).toHaveCount(4);
    await expect(page.getByRole('listitem').first()).toContainText('Esperando en la barra');
    await expect(page.getByRole('listitem').first()).toHaveAttribute('data-reached', 'true');
    await expect(page.getByRole('listitem').last()).toHaveAttribute('data-reached', 'false');
  });

  // Criterion 4: they closed the page and came back to the same link later.
  test('still shows the order when the link is opened again', async ({ page, request }) => {
    const link = await anOrderJustPaid(page, request);
    const code = await page.getByTestId('order-code').textContent();

    await page.goto(menuPath);
    await page.goto(link);

    await expect(page.getByTestId('order-code')).toHaveText(code!);
  });

  /**
   * Criterion 5, and the reason the token exists. The codes run in order, so
   * the order next door is this one minus one — and knowing that gets nobody
   * anywhere without the other half of the link.
   */
  test('does not open for somebody who only knows the code', async ({ page, request }) => {
    const link = await anOrderJustPaid(page, request);
    const [, codeAndToken] = link.split('/orders/');
    const [code] = codeAndToken.split('/');

    // The right code, a token somebody made up.
    await page.goto(`/${seededVenueSlug}/orders/${code}/${'0'.repeat(32)}`);

    await expect(page.getByRole('alert')).toContainText(/no lleva a ningún pedido/i);
    await expect(page.getByTestId('order-code')).toHaveCount(0);
  });

  test('does not open for a code that belongs to nobody', async ({ page, request }) => {
    const link = await anOrderJustPaid(page, request);
    const [, codeAndToken] = link.split('/orders/');
    const [, token] = codeAndToken.split('/');

    await page.goto(`/${seededVenueSlug}/orders/Z-9999/${token}`);

    await expect(page.getByRole('alert')).toContainText(/no lleva a ningún pedido/i);
  });

  /*
   * Criterion 3 — that the screen catches up on its own — is not tested here,
   * and deliberately. The bar has no screens in this sprint, so nothing can
   * move an order along: proving it end to end would mean either an endpoint
   * that exists only to be tested or a test that reaches into the database.
   * What the polling does is covered in tracking.store.spec.ts — that it asks
   * again, notices the change, stops when the order is over and rests while
   * nobody is looking — and the criterion is shown by hand in the demo, moving
   * the status in the database, which is what the backlog says.
   */
});
