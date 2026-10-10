import { devices, expect, test } from '@playwright/test';
import type { APIRequestContext, Browser, Page } from '@playwright/test';
import { categoryIdNamed } from './categories';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';

/**
 * US-37, end to end against the real API: the stock belongs to the night, and
 * the administrator reads it and moves it from the night's own screen. The
 * night is the one night.setup.ts left on.
 *
 * Criterion 2 — the order that asks for more than is left is refused — is
 * proved against a real database in the integration suite; what this adds is
 * the second actor: a sale landing while the administrator's screen is open.
 */
const loginPath = `/${seededVenueSlug}/staff/login`;

/** Unique per run: the development database keeps everything every run created. */
function aNewProduct(): string {
  return `E2E ${Date.now().toString(36)}${Math.random().toString(36).slice(2, 5)}`;
}

async function adminToken(request: APIRequestContext): Promise<string> {
  const login = await request.post(`/api/${seededVenueSlug}/auth/login`, {
    data: { username: seededAdminUsername, password: seededAdminPassword() },
  });
  expect(login.ok()).toBe(true);

  return ((await login.json()) as { token: string }).token;
}

async function loadProduct(
  request: APIRequestContext,
  token: string,
  name: string,
  initialStock: number,
): Promise<void> {
  const created = await request.post('/api/staff/products', {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      name,
      description: 'Cargado por la prueba',
      price: 4500,
      initialStock,
      categoryId: await categoryIdNamed(request, token, 'Tragos'),
    },
  });

  expect(created.status()).toBe(201);
}

/** The night that is on now: the first one in the listing that already started. */
async function tonightsNightId(request: APIRequestContext, token: string): Promise<string> {
  const listed = await request.get('/api/nights', {
    headers: { Authorization: `Bearer ${token}` },
  });
  const nights = (await listed.json()) as { id: string; startsAt: string; endsAt: string }[];
  const now = Date.now();
  const tonight = nights.find(
    (night) => Date.parse(night.startsAt) <= now && now < Date.parse(night.endsAt),
  );

  expect(tonight, 'night.setup.ts should have left a night on').toBeDefined();

  return tonight!.id;
}

async function logInAsTheAdministrator(page: Page): Promise<void> {
  await page.goto(loginPath);
  await page.getByRole('textbox', { name: /usuario/i }).fill(seededAdminUsername);
  await page.getByRole('textbox', { name: /contraseña/i }).fill(seededAdminPassword());
  await page.getByRole('button', { name: /entrar/i }).click();
  await expect(page).toHaveURL(/\/staff\/products$/);
}

/** A customer on their phone ordering and paying some of a product. */
async function aCustomerBuys(browser: Browser, name: string, units: number): Promise<void> {
  const phone = await browser.newContext({
    ...devices['iPhone 13'],
    baseURL: test.info().project.use.baseURL,
  });
  const customer = await phone.newPage();

  await customer.goto(`/${seededVenueSlug}/menu`);
  await customer.getByRole('searchbox', { name: /buscar trago/i }).fill(name);

  const add = customer.getByRole('button', { name: new RegExp(`agregar ${name}`, 'i') });
  for (let unit = 0; unit < units; unit++) await add.click();
  await expect(customer.getByTestId(`quantity-${name}`)).toHaveText(`${units}`);

  await customer.getByTestId('order-summary').click();
  await customer.getByRole('link', { name: /ir a pagar/i }).click();
  await customer.getByRole('textbox', { name: /nombre/i }).fill('María Quadro');
  await customer.getByRole('button', { name: /pagar/i }).click();
  await expect(customer.getByTestId('order-code')).toBeVisible({ timeout: 15000 });

  await phone.close();
}

test.describe('Night stock', () => {
  // Criterion 1: the stock starts from the product's own number, and the
  // administrator adds the units that arrived.
  test('adds the units that arrived to what the night has', async ({ page, request }) => {
    const token = await adminToken(request);
    const name = aNewProduct();
    await loadProduct(request, token, name, 5);
    const night = await tonightsNightId(request, token);

    await logInAsTheAdministrator(page);
    await page.goto(`/${seededVenueSlug}/staff/nights/${night}`);

    const row = page.getByRole('listitem', { name });
    await expect(row).toContainText('5');

    await row.getByRole('spinbutton').fill('12');
    await row.getByRole('button', { name: /sumar/i }).click();

    // Added, not set: 5 and 12 make 17.
    await expect(row).toContainText('17');
  });

  // Two actors: the screen shows 5, a customer buys 3 from their phone, and
  // taking away the 5 the screen still shows would leave the night below zero.
  // Nothing is written, and the row says how many are left.
  test('refuses to take away what sales made meanwhile already used', async ({
    page,
    browser,
    request,
  }) => {
    const token = await adminToken(request);
    const name = aNewProduct();
    await loadProduct(request, token, name, 5);
    const night = await tonightsNightId(request, token);

    await logInAsTheAdministrator(page);
    await page.goto(`/${seededVenueSlug}/staff/nights/${night}`);

    const row = page.getByRole('listitem', { name });
    await expect(row).toContainText('5');

    await aCustomerBuys(browser, name, 3);

    await row.getByRole('spinbutton').fill('5');
    await row.getByRole('button', { name: /restar/i }).click();

    await expect(row.getByRole('alert')).toContainText(/se vendió mientras tanto/i);
    await expect(row).toContainText('2');
  });
});
