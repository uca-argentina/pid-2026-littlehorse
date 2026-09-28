import { expect, test } from '@playwright/test';
import type { APIRequestContext, Page } from '@playwright/test';
import { categoryIdNamed } from './categories';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';

/**
 * US-15, end to end against the real API, a real database and a real SignalR
 * connection: a paid order shows up on the bar's own board, with what a
 * bartender needs to make it, and it shows up live — nobody touches the
 * tablet for it to appear. Nobody but the bar's own account gets to see it.
 */
const loginPath = `/${seededVenueSlug}/staff/login`;
const kdsPath = `/${seededVenueSlug}/staff/kds`;
const menuPath = `/${seededVenueSlug}/menu`;

/** Unique per run: the development database keeps everything every run created. */
function aNewUsername(): string {
  return `e2e.${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
}

function aNewProduct(): string {
  return `E2E ${Date.now().toString(36)}${Math.random().toString(36).slice(2, 5)}`;
}

/**
 * Letters only, in two words: the checkout screen's own full-name pattern
 * (FULL_NAME in checkout.page.ts) refuses a name with a digit in it, which is
 * what every other "unique per run" helper here produces.
 */
function aFullName(): string {
  const word = () =>
    Array.from({ length: 8 }, () => String.fromCharCode(97 + Math.floor(Math.random() * 26))).join(
      '',
    );

  return `Cliente ${word()}`;
}

const aNewPassword = 'a-long-enough-password';

async function adminToken(request: APIRequestContext): Promise<string> {
  const login = await request.post(`/api/${seededVenueSlug}/auth/login`, {
    data: { username: seededAdminUsername, password: seededAdminPassword() },
  });

  return ((await login.json()) as { token: string }).token;
}

/** A fresh Kds account, the way an administrator would create one. */
async function aKdsAccount(request: APIRequestContext): Promise<string> {
  const username = aNewUsername();

  const created = await request.post('/api/staff/users', {
    headers: { Authorization: `Bearer ${await adminToken(request)}` },
    data: { username, password: aNewPassword, role: 'Kds' },
  });

  expect(created.status()).toBe(201);

  return username;
}

async function loadProduct(request: APIRequestContext, name: string): Promise<void> {
  const token = await adminToken(request);

  const created = await request.post('/api/staff/products', {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      name,
      description: 'Cargado por la prueba',
      price: 4500,
      stock: 20,
      categoryId: await categoryIdNamed(request, token, 'Tragos'),
    },
  });

  expect(created.status()).toBe(201);
}

async function logIn(page: Page, username: string, password: string): Promise<void> {
  await page.goto(loginPath);
  await page.getByRole('textbox', { name: /usuario/i }).fill(username);
  await page.getByRole('textbox', { name: /contraseña/i }).fill(password);
  await page.getByRole('button', { name: /entrar/i }).click();
}

/** Places and pays for one drink as a customer, digital — the only method there is today. */
async function anOrderPaidBy(
  page: Page,
  request: APIRequestContext,
  customerName: string,
): Promise<string> {
  const drinkName = aNewProduct();
  await loadProduct(request, drinkName);

  await page.goto(menuPath);
  await page.getByRole('searchbox', { name: /buscar trago/i }).fill(drinkName);
  await page.getByRole('button', { name: new RegExp(`agregar ${drinkName}`, 'i') }).click();
  await page.getByTestId('order-summary').click();
  await page.getByRole('link', { name: /ir a pagar/i }).click();

  await page.getByRole('textbox', { name: /nombre/i }).fill(customerName);
  await page.getByRole('button', { name: /pagar/i }).click();
  await expect(page.getByTestId('order-code')).toBeVisible({ timeout: 15000 });

  return drinkName;
}

test.describe('KDS board', () => {
  test('shows a paid order under Nuevos, with who ordered and what they ordered', async ({
    page,
    browser,
    request,
  }) => {
    const username = await aKdsAccount(request);
    const customerName = aFullName();

    const customer = await browser.newPage();
    const drinkName = await anOrderPaidBy(customer, request, customerName);
    await customer.close();

    await logIn(page, username, aNewPassword);

    await expect(page).toHaveURL(new RegExp(`${kdsPath}$`));
    await expect(page.getByRole('banner')).toContainText(username);

    const card = page.getByRole('group', { name: 'Nuevos' }).getByRole('article').filter({
      hasText: customerName,
    });

    await expect(card).toContainText(drinkName);
  });

  // US-16, criteria 1 and 4, through the real API: the order moves column
  // and comes back, and the board redraws both times without a reload.
  test('takes an order into En preparación and hands it back to Nuevos', async ({
    page,
    browser,
    request,
  }) => {
    const username = await aKdsAccount(request);
    const customerName = aFullName();

    const customer = await browser.newPage();
    await anOrderPaidBy(customer, request, customerName);
    await customer.close();

    await logIn(page, username, aNewPassword);
    await expect(page).toHaveURL(new RegExp(`${kdsPath}$`));

    const column = (name: string) =>
      page.getByRole('group', { name }).getByRole('article').filter({ hasText: customerName });

    await column('Nuevos')
      .getByRole('button', { name: /^Imprimir / })
      .click();
    await expect(column('En preparación')).toBeVisible();
    await expect(column('Nuevos')).toHaveCount(0);

    await column('En preparación')
      .getByRole('button', { name: /a la cola$/ })
      .click();
    await expect(column('Nuevos')).toBeVisible();
    await expect(column('En preparación')).toHaveCount(0);
  });

  // The one thing a unit test cannot prove: the hub, the token in the
  // connection, and the proxy all actually agree with each other.
  test('shows a new order live, without anybody reloading the tablet', async ({
    page,
    browser,
    request,
  }) => {
    const username = await aKdsAccount(request);
    const customerName = aFullName();

    await logIn(page, username, aNewPassword);
    await expect(page).toHaveURL(new RegExp(`${kdsPath}$`));

    const customer = await browser.newPage();
    await anOrderPaidBy(customer, request, customerName);
    await customer.close();

    await expect(page.getByText(customerName)).toBeVisible({ timeout: 15000 });
  });

  // Criterion 3: not even by typing the address, once signed in as anyone else.
  test('turns away an administrator', async ({ page }) => {
    await page.goto(loginPath);
    await page.getByRole('textbox', { name: /usuario/i }).fill(seededAdminUsername);
    await page.getByRole('textbox', { name: /contraseña/i }).fill(seededAdminPassword());
    await page.getByRole('button', { name: /entrar/i }).click();

    await page.goto(kdsPath);

    await expect(page).not.toHaveURL(new RegExp(`${kdsPath}$`));
  });

  // Criterion 4 is covered in KdsBoardPage's own Vitest spec, with a
  // controlled empty response: the shared development database this suite
  // runs against is never reset between runs, so nothing here can promise the
  // queue is actually empty — only a mock can.
});
