import { expect, test } from '@playwright/test';
import type { Page } from '@playwright/test';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';

/**
 * US-06, end to end against the real API and the seeded database: an
 * administrator loads a product, it shows up in the listing with its price
 * and stock, and the form stops what the domain would reject before the
 * request leaves the laptop.
 *
 * The other half of criterion 1 — the product on the menu the customer sees —
 * is verified by US-09's spec, which is where that screen exists.
 */
const loginPath = `/${seededVenueSlug}/staff/login`;
const productsPath = `/${seededVenueSlug}/staff/products`;

/**
 * The development database is not reset between runs, and a product name is
 * unique per venue: reusing a fixed one makes the second run fail on a
 * conflict that has nothing to do with the change being tested.
 */
function aNewProductName(): string {
  return `E2E ${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
}

async function logInAsTheAdministrator(page: Page): Promise<void> {
  await page.goto(loginPath);
  await page.getByRole('textbox', { name: /usuario/i }).fill(seededAdminUsername);
  await page.getByRole('textbox', { name: /contraseña/i }).fill(seededAdminPassword());
  await page.getByRole('button', { name: /entrar/i }).click();
  await expect(page).toHaveURL(new RegExp(`/${seededVenueSlug}/staff$`));
}

async function fillTheForm(page: Page, name: string, price: string, stock: string): Promise<void> {
  await page.getByRole('textbox', { name: /^nombre/i }).fill(name);
  await page
    .getByRole('textbox', { name: /descripción/i })
    .fill('Gin, tónica y una rodaja de lima.');
  await page.getByRole('spinbutton', { name: /precio/i }).fill(price);
  await page.getByRole('spinbutton', { name: /stock/i }).fill(stock);
  await page.getByRole('button', { name: /crear producto/i }).click();
}

test.describe('Products', () => {
  test('adds a product that then shows up in the listing', async ({ page }) => {
    const name = aNewProductName();

    await logInAsTheAdministrator(page);

    // The way in is the home screen, not a typed address: an administrator who
    // has to be told the URL has no administration screen at all.
    await page.getByRole('link', { name: /productos/i }).click();
    await expect(page).toHaveURL(new RegExp(`${productsPath}$`));

    await page.getByRole('link', { name: /nuevo producto/i }).click();
    await fillTheForm(page, name, '4500', '20');

    // Criterion 1, as far as this story goes: it is on the venue's menu.
    await expect(page).toHaveURL(new RegExp(`${productsPath}$`));
    const row = page.getByRole('listitem').filter({ hasText: name });
    await expect(row).toBeVisible();
    await expect(row).toContainText('4.500');
    await expect(row).toContainText('20 en stock');
  });

  // Decided on 2026-09-14: stock at zero sells the product out on its own.
  test('shows a product loaded with no stock as sold out', async ({ page }) => {
    const name = aNewProductName();

    await logInAsTheAdministrator(page);
    await page.goto(`${productsPath}/new`);
    await fillTheForm(page, name, '4500', '0');

    await expect(page.getByRole('listitem').filter({ hasText: name })).toContainText(/sin stock/i);
  });

  // Criterion 2. The form stops it, so the venue's connection is never part of
  // finding out that the price was left at zero.
  test('does not save a price of zero, and says why', async ({ page }) => {
    await logInAsTheAdministrator(page);

    await page.goto(`${productsPath}/new`);
    await fillTheForm(page, aNewProductName(), '0', '20');

    await expect(page.getByText(/mayor a cero/i)).toBeVisible();
    await expect(page).toHaveURL(new RegExp(`${productsPath}/new$`));
  });

  // Criterion 3.
  test('does not save without a name', async ({ page }) => {
    await logInAsTheAdministrator(page);

    await page.goto(`${productsPath}/new`);
    await fillTheForm(page, '   ', '4500', '20');

    await expect(page.getByText(/ponele un nombre/i)).toBeVisible();
    await expect(page).toHaveURL(new RegExp(`${productsPath}/new$`));
  });

  // The administrator has to be able to fix it on the spot, so the message has
  // to say what is wrong rather than that something failed.
  test('refuses a name that this venue already sells', async ({ page }) => {
    const name = aNewProductName();

    await logInAsTheAdministrator(page);
    await page.goto(`${productsPath}/new`);
    await fillTheForm(page, name, '4500', '20');
    await expect(page.getByRole('listitem').filter({ hasText: name })).toBeVisible();

    await page.goto(`${productsPath}/new`);
    await fillTheForm(page, name.toLowerCase(), '4500', '20');

    await expect(page.getByRole('alert')).toHaveText(/ya hay un producto con ese nombre/i);
    await expect(page).toHaveURL(new RegExp(`${productsPath}/new$`));
  });
});
