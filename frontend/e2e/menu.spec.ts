import { expect, test } from '@playwright/test';
import type { APIRequestContext, Page } from '@playwright/test';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';

/**
 * US-09, end to end: somebody scans the QR on the wall and reads the menu. No
 * session is ever created here, which is the point — every other spec in this
 * suite starts by signing in, and this one must never need to.
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

function card(page: Page, name: string) {
  return page.getByRole('listitem').filter({ hasText: name });
}

test.describe('Menu', () => {
  test('shows the venue menu to somebody who just scanned the QR', async ({ page, request }) => {
    const name = aNewProduct();
    await loadProduct(request, name, 20);

    await page.goto(menuPath);

    // Criterion 1: the venue of the address, named on screen, because whoever
    // scanned the QR never typed where they are.
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Bar Alfa');
    await expect(card(page, name)).toBeVisible();
    await expect(card(page, name)).toContainText('$ 4.500,00');
  });

  /**
   * Criterion 2, and the one worth running in a real browser: nothing on this
   * path asks for an account, a password or an install, and no session is left
   * behind either.
   */
  test('never asks for an account, a password or an install', async ({ page, request }) => {
    await loadProduct(request, aNewProduct(), 5);

    await page.goto(menuPath);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    await expect(page.getByRole('textbox', { name: /contraseña/i })).toHaveCount(0);
    await expect(page.getByRole('button', { name: /entrar|instalar/i })).toHaveCount(0);
    await expect(page.getByText(/crear una cuenta|iniciá tu turno/i)).toHaveCount(0);

    const stored = await page.evaluate(() => sessionStorage.getItem('drinkit.staff-session'));

    expect(stored).toBeNull();
  });

  // A rule of the story: sold out is obvious at a glance, and stays on the menu
  // rather than disappearing, which would read as a mistake.
  test('keeps what ran out on the menu, marked', async ({ page, request }) => {
    const name = aNewProduct();
    await loadProduct(request, name, 0);

    await page.goto(menuPath);

    await expect(card(page, name)).toBeVisible();
    await expect(card(page, name)).toContainText(/sin stock/i);
  });

  test('finds one drink among the whole menu', async ({ page, request }) => {
    const name = aNewProduct();
    await loadProduct(request, name, 10);

    await page.goto(menuPath);
    await page.getByRole('searchbox', { name: /buscar trago/i }).fill(name);

    await expect(page.getByRole('listitem')).toHaveCount(1);
    await expect(card(page, name)).toBeVisible();
  });

  // A slug nobody serves. Told apart from a venue that exists and has nothing
  // loaded, and from a connection that dropped: a retry here could never work.
  test('says the address belongs to no venue, without offering to retry', async ({ page }) => {
    await page.goto('/bar-que-no-existe/menu');

    await expect(page.getByRole('alert')).toContainText(/no es de ningún boliche/i);
    await expect(page.getByRole('button', { name: /reintentar/i })).toHaveCount(0);
  });

  /**
   * The one the review caught. An administrator of one venue opening another
   * venue's menu was served their own venue's products under the other venue's
   * name: the API preferred the token's venue over the slug, on a page that is
   * addressed by slug and by nothing else.
   *
   * Only one venue exists in dev today, so this cannot prove the slug wins
   * over a *different* venue's token — that half of the regression is what
   * `VenueResolutionMiddlewareTests.InvokeAsync_WhenTheEndpointIsPublic_TheSlugWinsOverTheToken`
   * actually covers. What this test owns is that a leftover staff session
   * does not break the customer's page at all.
   */
  test('shows the venue in the address even to somebody signed in elsewhere', async ({
    page,
    request,
  }) => {
    const name = aNewProduct();
    await loadProduct(request, name, 8);

    await page.goto(`/${seededVenueSlug}/staff/login`);
    await page.getByRole('textbox', { name: /usuario/i }).fill(seededAdminUsername);
    await page.getByLabel('Contraseña', { exact: true }).fill(seededAdminPassword());
    await page.getByRole('button', { name: /entrar/i }).click();
    // Wherever their role lands them: what matters is that a session now exists.
    await expect(page).toHaveURL(new RegExp(`/${seededVenueSlug}/staff`));

    await page.goto(menuPath);

    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Bar Alfa');
    await expect(card(page, name)).toBeVisible();
  });

  /**
   * US-10, the half that lives on the menu. The order screen is not built yet,
   * so the strip at the bottom is a summary and nothing more.
   */
  test('adds a drink to the order and keeps it after a reload', async ({ page, request }) => {
    const name = aNewProduct();
    await loadProduct(request, name, 12);

    await page.goto(menuPath);
    await page.getByRole('searchbox', { name: /buscar trago/i }).fill(name);

    await expect(page.getByTestId('order-summary')).toHaveCount(0);

    await page.getByRole('button', { name: new RegExp(`agregar ${name}`, 'i') }).click();
    await expect(page.getByTestId('order-summary')).toContainText('1 ítem');
    await expect(page.getByTestId('order-summary')).toContainText('$ 4.500,00');

    await page.getByRole('button', { name: new RegExp(`agregar ${name}`, 'i') }).click();
    await expect(page.getByTestId('order-summary')).toContainText('2 ítems');

    // Criterion 4, in a real browser: they took a phone call and came back.
    await page.reload();

    await expect(page.getByTestId('order-summary')).toContainText('2 ítems');
    await expect(page.getByTestId('order-summary')).toContainText('$ 9.000,00');
  });

  // US-10, criterion 2, without leaving the menu: the count on the card and the
  // total at the bottom move together.
  test('lowers the quantity from the card itself', async ({ page, request }) => {
    const name = aNewProduct();
    await loadProduct(request, name, 12);

    await page.goto(menuPath);
    await page.getByRole('searchbox', { name: /buscar trago/i }).fill(name);

    const add = page.getByRole('button', { name: new RegExp(`agregar ${name}`, 'i') });
    const take = page.getByRole('button', { name: new RegExp(`quitar un ${name}`, 'i') });

    // Nothing to take out until there is something in the order.
    await expect(take).toHaveCount(0);

    await add.click();
    await add.click();
    await expect(page.getByTestId(`quantity-${name}`)).toHaveText('2');

    await take.click();
    await expect(page.getByTestId(`quantity-${name}`)).toHaveText('1');
    await expect(page.getByTestId('order-summary')).toContainText('$ 4.500,00');

    // The last one out takes the drink out of the order, and the card goes back
    // to offering only the way in.
    await take.click();
    await expect(page.getByTestId(`quantity-${name}`)).toHaveCount(0);
    await expect(take).toHaveCount(0);
    await expect(page.getByTestId('order-summary')).toHaveCount(0);
  });

  // The order belongs to the venue it was built in, and nothing carries it out.
  test('leaves the order behind when another venue is opened', async ({ page, request }) => {
    const name = aNewProduct();
    await loadProduct(request, name, 6);

    await page.goto(menuPath);
    await page.getByRole('searchbox', { name: /buscar trago/i }).fill(name);
    await page.getByRole('button', { name: new RegExp(`agregar ${name}`, 'i') }).click();
    await expect(page.getByTestId('order-summary')).toContainText('1 ítem');

    await page.goto('/bar-que-no-existe/menu');

    await expect(page.getByTestId('order-summary')).toHaveCount(0);
  });
});
