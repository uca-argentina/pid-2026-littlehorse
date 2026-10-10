import { devices, expect, test } from '@playwright/test';
import type { APIRequestContext, Browser, BrowserContext, Page } from '@playwright/test';
import { categoryIdNamed } from './categories';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';
import { joinTonight } from './nights';

/**
 * US-24 to US-26, end to end with three actors against the real API: the
 * customer confirms paying in cash and is sent to the till with a code, the
 * order stays off the bar's board, the cashier finds it by that code and
 * collects it, and from there the customer's screen and the bar's board both
 * move on without anybody touching them.
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

async function anAccount(request: APIRequestContext, role: 'Kds' | 'Cashier'): Promise<string> {
  const username = `e2e.${unique()}`;

  const token = await adminToken(request);
  const created = await request.post('/api/staff/users', {
    headers: { Authorization: `Bearer ${token}` },
    data: { username, password, role },
  });

  expect(created.status()).toBe(201);
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
      initialStock: 20,
      categoryId: await categoryIdNamed(request, token, 'Tragos'),
    },
  });

  expect(created.status()).toBe(201);

  return name;
}

async function signedIn(
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

  return { context, page };
}

test.describe('paying in cash at the till', () => {
  test('sends the order to the bar only once the cashier collects it', async ({
    browser,
    baseURL,
    request,
  }) => {
    const phone = await browser.newContext({ ...devices['iPhone 13'], baseURL });
    const customer = await phone.newPage();
    const [drink, cashierName, kdsName] = await Promise.all([
      aProduct(request),
      anAccount(request, 'Cashier'),
      anAccount(request, 'Kds'),
    ]);
    const customerName = aFullName();

    // The till is already open, as it would be on a night: nobody reloads it.
    const till = await signedIn(browser, baseURL, cashierName);
    await expect(till.page).toHaveURL(/\/staff\/cashier$/);
    await expect(
      till.page
        .getByRole('list', { name: /cobros de tu turno/i })
        .or(till.page.getByText(/todavía no cobraste/i)),
    ).toBeVisible();

    // The customer confirms, choosing cash (US-24), and is sent to the till (US-25).
    await customer.goto(menuPath);
    await customer.getByRole('searchbox', { name: /buscar trago/i }).fill(drink);
    await customer.getByRole('button', { name: new RegExp(`agregar ${drink}`, 'i') }).click();
    await customer.getByTestId('order-summary').click();
    await customer.getByRole('link', { name: /ir a pagar/i }).click();
    await customer.getByRole('textbox', { name: /nombre/i }).fill(customerName);
    await customer.getByLabel(/efectivo/i).check();
    await customer.getByRole('button', { name: /confirmar pedido/i }).click();

    const code = (await customer.getByTestId('order-code').textContent({ timeout: 15000 }))!.trim();
    await expect(customer.getByText(/mostrale este código al cajero/i)).toBeVisible();
    await expect(customer.getByRole('status')).toContainText(/pagar en la caja/i);
    await expect(customer.getByRole('img', { name: /qr para pagar en la caja/i })).toBeVisible();
    // What the QR carries: the token at the end of the customer's own address.
    const token = new URL(customer.url()).pathname.split('/').at(-1)!;

    // Not on the bar's board while nobody has paid (US-24, criterion 3).
    const bar = await signedIn(browser, baseURL, kdsName);
    await expect(bar.page).toHaveURL(/\/staff\/kds$/);
    await expect(bar.page.getByText(/la cola|nuevos/i).first()).toBeVisible();
    await expect(bar.page.getByText(customerName)).toHaveCount(0);

    // It shows up in "Por cobrar" on its own, over the till's hub; then the
    // cashier reads the QR and collects it (US-26).
    await expect(till.page.getByRole('list', { name: /por cobrar/i })).toContainText(code);
    const entry = till.page.getByLabel(/escaneá o escribí el código/i);

    // A USB reader types what the QR carries and presses Enter.
    await entry.fill(token);
    await entry.press('Enter');
    const order = till.page.getByRole('region', { name: new RegExp(`pedido ${code}`, 'i') });
    await expect(order).toContainText(drink);
    await till.page.getByRole('button', { name: /confirmar cobro/i }).click();
    await expect(till.page.getByRole('status')).toContainText(/cobrado/i);
    await expect(
      till.page.getByRole('button', { name: new RegExp(`cobrar ${code}`, 'i') }),
    ).toHaveCount(0);
    await expect(till.page.getByRole('list', { name: /cobros de tu turno/i })).toContainText(code);

    // Nobody touches the phone or the tablet (US-25, criterion 3).
    await expect(customer.getByRole('status')).toContainText(/en la cola/i, {
      timeout: 15000,
    });
    await expect(bar.page.getByText(customerName)).toBeVisible({ timeout: 15000 });

    // Typing the same code again is not a second charge (US-26, criterion 3).
    await entry.fill(code.toLowerCase());
    await entry.press('Enter');
    await expect(till.page.getByRole('alert')).toContainText(/ya está pago/i);
    await expect(till.page.getByRole('button', { name: /confirmar cobro/i })).toHaveCount(0);

    await phone.close();
    await bar.context.close();
    await till.context.close();
  });
});
