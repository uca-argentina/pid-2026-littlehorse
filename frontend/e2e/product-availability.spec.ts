import { devices, expect, test } from '@playwright/test';
import type { APIRequestContext, Browser, Page } from '@playwright/test';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';

/**
 * US-07, the two screens at once: the bar's tablet turns a drink off and the
 * customer's phone stops being able to order it.
 *
 * Both halves live in one spec on purpose. The administration side alone is
 * already covered by products.spec.ts; what only a run like this can prove is
 * that flipping the switch on one device changes what the other one is allowed
 * to do — which is the whole story, and the part no unit test reaches.
 */
const menuPath = `/${seededVenueSlug}/menu`;
const loginPath = `/${seededVenueSlug}/staff/login`;
const productsPath = `/${seededVenueSlug}/staff/products`;

/** Unique per run: the development database keeps everything every run created. */
function aNewProduct(): string {
  return `E2E ${Date.now().toString(36)}${Math.random().toString(36).slice(2, 5)}`;
}

/**
 * Loaded through the API and not through the form. Getting to the state this
 * spec is about costs five taps on a screen another spec already tests, and
 * every one of them is a way for this test to fail for somebody else's reason.
 */
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

async function theCustomersPhone(browser: Browser, baseURL: string | undefined): Promise<Page> {
  const context = await browser.newContext({ ...devices['iPhone 13'], baseURL });

  return context.newPage();
}

/** Signed in and on the menu, which is where signing in lands. */
async function theBarsTablet(browser: Browser, baseURL: string | undefined): Promise<Page> {
  const context = await browser.newContext({ viewport: { width: 820, height: 1180 }, baseURL });
  const page = await context.newPage();

  await page.goto(loginPath);
  await page.getByRole('textbox', { name: /usuario/i }).fill(seededAdminUsername);
  await page.getByRole('textbox', { name: /contraseña/i }).fill(seededAdminPassword());
  await page.getByRole('button', { name: /entrar/i }).click();
  await expect(page).toHaveURL(new RegExp(`${productsPath}$`));

  return page;
}

/** The row of the listing for one product, found the way a person finds it. */
async function theRowFor(tablet: Page, name: string) {
  await tablet.getByRole('searchbox', { name: /buscar producto/i }).fill(name);

  return tablet.getByRole('listitem').filter({ hasText: name });
}

/** The card of the menu for one product, found the same way. */
async function theCardFor(phone: Page, name: string) {
  await phone.getByRole('searchbox', { name: /buscar trago/i }).fill(name);

  return phone.getByRole('listitem').filter({ hasText: name });
}

test.describe('Product availability', () => {
  /**
   * Criteria 1 and 2, across two devices: off, and then back on with nothing
   * loaded again by hand.
   */
  test('stops the customer ordering a drink the bar turned off, and lets them again', async ({
    browser,
    baseURL,
    request,
  }) => {
    const name = aNewProduct();
    await loadProduct(request, name);

    const phone = await theCustomersPhone(browser, baseURL);
    const tablet = await theBarsTablet(browser, baseURL);

    await phone.goto(menuPath);
    await expect(
      (await theCardFor(phone, name)).getByRole('button', {
        name: new RegExp(`agregar ${name}`, 'i'),
      }),
    ).toBeVisible();

    const row = await theRowFor(tablet, name);
    await row.getByRole('switch').click();
    await expect(row.getByRole('switch')).not.toBeChecked();

    // Criterion 1. Shown and not hidden: a card that disappeared would read as
    // a mistake and send somebody to ask at the bar, which is the walk this
    // whole product exists to avoid.
    await phone.reload();
    const card = await theCardFor(phone, name);
    await expect(card).toBeVisible();
    await expect(card).toContainText(/sin stock por ahora/i);
    await expect(
      card.getByRole('button', { name: new RegExp(`agregar ${name}`, 'i') }),
    ).toHaveCount(0);

    // Criterion 2: they restocked, and nobody had to load the drink again.
    await row.getByRole('switch').click();
    await expect(row.getByRole('switch')).toBeChecked();

    await phone.reload();
    await expect(
      (await theCardFor(phone, name)).getByRole('button', {
        name: new RegExp(`agregar ${name}`, 'i'),
      }),
    ).toBeVisible();
  });

  /**
   * Criterion 3: the drink was already in somebody's order when the bar turned
   * it off. They find out when they try to pay, and they are not charged for
   * something nobody is going to make.
   */
  test('warns the customer at the till when a drink in their order was turned off', async ({
    browser,
    baseURL,
    request,
  }) => {
    const name = aNewProduct();
    await loadProduct(request, name);

    const phone = await theCustomersPhone(browser, baseURL);
    const tablet = await theBarsTablet(browser, baseURL);

    await phone.goto(menuPath);
    await (
      await theCardFor(phone, name)
    )
      .getByRole('button', { name: new RegExp(`agregar ${name}`, 'i') })
      .click();
    await expect(phone.getByTestId('order-summary')).toBeVisible();

    // The ice machine breaks while the phone is still deciding.
    const row = await theRowFor(tablet, name);
    await row.getByRole('switch').click();
    await expect(row.getByRole('switch')).not.toBeChecked();

    await phone.getByTestId('order-summary').click();
    await phone.getByRole('link', { name: /ir a pagar/i }).click();
    await phone.getByRole('textbox', { name: /nombre/i }).fill('María Quadro');
    await phone.getByRole('button', { name: /pagar/i }).click();

    // Told which drink, and sent to the one screen where it can be taken out.
    await expect(phone.getByRole('alert')).toContainText(name, { timeout: 15000 });
    await expect(phone.getByRole('link', { name: /revisar el pedido/i })).toBeVisible();

    // And not charged: no code was handed out and the phone is still at the
    // till, not on the screen that follows a paid order.
    await expect(phone.getByTestId('order-code')).toHaveCount(0);
    await expect(phone).toHaveURL(new RegExp(`/${seededVenueSlug}/checkout$`));
  });
});
