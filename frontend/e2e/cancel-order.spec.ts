import { devices, expect, test } from '@playwright/test';
import type { APIRequestContext, Browser, BrowserContext, Page } from '@playwright/test';
import { categoryIdNamed } from './categories';
import { joinTonight } from './nights';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';

/**
 * US-23, end to end with two actors against the real API: an order waiting to
 * be paid in cash is canceled — by the customer from their phone, or by the
 * cashier because they left without paying — and the other side hears of it
 * without anybody reloading.
 */
const loginPath = `/${seededVenueSlug}/staff/login`;
const menuPath = `/${seededVenueSlug}/menu`;
const password = 'a-long-enough-password';

function unique(): string {
  return `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
}

/** Letters only, in two words: the checkout refuses a name with a digit in it. */
function aFullName(): string {
  const word = () =>
    Array.from({ length: 8 }, () => String.fromCharCode(97 + Math.floor(Math.random() * 26))).join(
      '',
    );

  return `Cliente ${word()}`;
}

async function adminToken(request: APIRequestContext): Promise<string> {
  const login = await request.post(`/api/${seededVenueSlug}/auth/login`, {
    data: { username: seededAdminUsername, password: seededAdminPassword() },
  });

  return ((await login.json()) as { token: string }).token;
}

async function aCashierAccount(request: APIRequestContext): Promise<string> {
  const username = `e2e.${unique()}`;

  const token = await adminToken(request);
  const created = await request.post('/api/staff/users', {
    headers: { Authorization: `Bearer ${token}` },
    data: { username, password, role: 'Cashier' },
  });

  expect(created.status()).toBe(201);
  // US-35, criterion 4: a till outside tonight's night sees no orders.
  await joinTonight(request, token, ((await created.json()) as { id: string }).id);

  return username;
}

async function aProduct(request: APIRequestContext): Promise<string> {
  const name = `E2E ${unique()}`;
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

  return name;
}

/** The till, open and signed in, as it would be on a night. */
async function anOpenTill(
  browser: Browser,
  baseURL: string | undefined,
  username: string,
): Promise<{ context: BrowserContext; page: Page }> {
  const context = await browser.newContext({ viewport: { width: 820, height: 1180 }, baseURL });
  const page = await context.newPage();

  await page.goto(loginPath);
  await page.getByRole('textbox', { name: /usuario/i }).fill(username);
  await page.getByRole('textbox', { name: /contraseña/i }).fill(password);
  await page.getByRole('button', { name: /entrar/i }).click();
  await expect(page).toHaveURL(/\/staff\/cashier$/);

  return { context, page };
}

/** Confirms one drink choosing cash, and ends up on the order's own screen. Its code. */
async function anOrderToPayInCash(customer: Page, drink: string): Promise<string> {
  await customer.goto(menuPath);
  await customer.getByRole('searchbox', { name: /buscar trago/i }).fill(drink);
  await customer.getByRole('button', { name: new RegExp(`agregar ${drink}`, 'i') }).click();
  await customer.getByTestId('order-summary').click();
  await customer.getByRole('link', { name: /ir a pagar/i }).click();
  await customer.getByRole('textbox', { name: /nombre/i }).fill(aFullName());
  await customer.getByLabel(/efectivo/i).check();
  await customer.getByRole('button', { name: /confirmar pedido/i }).click();

  return (await customer.getByTestId('order-code').textContent({ timeout: 15000 }))!.trim();
}

test.describe('canceling an order waiting to be paid in cash', () => {
  test('the customer cancels it, and it leaves the till on its own', async ({
    browser,
    baseURL,
    request,
  }) => {
    const phone = await browser.newContext({ ...devices['iPhone 13'], baseURL });
    const customer = await phone.newPage();
    const [drink, cashierName] = await Promise.all([aProduct(request), aCashierAccount(request)]);
    const till = await anOpenTill(browser, baseURL, cashierName);

    const code = await anOrderToPayInCash(customer, drink);
    await expect(till.page.getByRole('list', { name: /por cobrar/i })).toContainText(code);

    await customer.getByRole('button', { name: /^cancelar pedido$/i }).click();
    await customer.getByRole('button', { name: /sí, cancelar/i }).click();

    await expect(customer.getByRole('alert')).toContainText(/tu pedido fue cancelado/i);
    await expect(customer.getByTestId('order-code')).toHaveCount(0);
    await expect(
      till.page.getByRole('button', { name: new RegExp(`cobrar ${code}`, 'i') }),
    ).toHaveCount(0);

    await phone.close();
    await till.context.close();
  });

  test('the cashier cancels it, and the customer’s screen says so on its own', async ({
    browser,
    baseURL,
    request,
  }) => {
    const phone = await browser.newContext({ ...devices['iPhone 13'], baseURL });
    const customer = await phone.newPage();
    const [drink, cashierName] = await Promise.all([aProduct(request), aCashierAccount(request)]);
    const till = await anOpenTill(browser, baseURL, cashierName);

    const code = await anOrderToPayInCash(customer, drink);

    await till.page.getByRole('button', { name: new RegExp(`cobrar ${code}`, 'i') }).click();
    await till.page.getByRole('button', { name: /^cancelar pedido$/i }).click();
    await till.page.getByRole('button', { name: /sí, cancelar pedido/i }).click();

    await expect(till.page.getByRole('status')).toContainText(
      new RegExp(`${code}.*cancelado`, 'i'),
    );
    await expect(customer.getByRole('alert')).toContainText(/tu pedido fue cancelado/i);

    await phone.close();
    await till.context.close();
  });
});
