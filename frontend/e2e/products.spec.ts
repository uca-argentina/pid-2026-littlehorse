import { join } from 'node:path';
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
  await expect(page).toHaveURL(new RegExp(`${productsPath}$`));
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

/** A one-pixel PNG: enough to be a picture, small enough to live in the repo. */
const aPhoto = join(__dirname, 'fixtures', 'pixel.png');

test.describe('Products', () => {
  test('adds a product that then shows up in the listing', async ({ page }) => {
    const name = aNewProductName();

    // Signing in lands here: the menu is what an administrator manages first.
    await logInAsTheAdministrator(page);

    await page.getByRole('link', { name: /nuevo producto/i }).click();
    await fillTheForm(page, name, '4500', '20');

    // Criterion 1, as far as this story goes: it is on the venue's menu.
    await expect(page).toHaveURL(new RegExp(`${productsPath}$`));
    const row = page.getByRole('listitem').filter({ hasText: name });
    await expect(row).toBeVisible();
    await expect(row).toContainText('4.500');
    await expect(row).toContainText('20 en stock');
  });

  // Criterion 4, the half this story owns: the photo goes up with the product
  // and the listing shows it from the storage, not from the API. The customer's
  // menu is US-09's spec.
  test('uploads the photo along with the product and shows it in the listing', async ({ page }) => {
    const name = aNewProductName();

    await logInAsTheAdministrator(page);
    await page.goto(`${productsPath}/new`);
    await page.getByLabel(/foto/i).setInputFiles(aPhoto);
    await expect(page.getByRole('img', { name: /vista previa/i })).toBeVisible();
    await fillTheForm(page, name, '4500', '20');

    const row = page.getByRole('listitem').filter({ hasText: name });
    await expect(row).toBeVisible();
    const picture = row.getByRole('img', { name });
    await expect(picture).toBeVisible();
    await expect(picture).toHaveAttribute('src', /product-images\/products\//);
  });

  // The API would answer 415 to this, after the whole file went up. The form
  // says so first, and nothing is created.
  test('does not save with a file that is not a picture', async ({ page }) => {
    await logInAsTheAdministrator(page);
    await page.goto(`${productsPath}/new`);
    await page.getByLabel(/foto/i).setInputFiles({
      name: 'menu.pdf',
      mimeType: 'application/pdf',
      buffer: Buffer.from('%PDF-1.4'),
    });
    await fillTheForm(page, aNewProductName(), '4500', '20');

    await expect(page.getByText(/JPEG, PNG o WebP/i)).toBeVisible();
    await expect(page).toHaveURL(new RegExp(`${productsPath}/new$`));
  });

  // Decided on 2026-09-14: stock at zero sells the product out on its own.
  test('shows a product loaded with no stock as sold out', async ({ page }) => {
    const name = aNewProductName();

    await logInAsTheAdministrator(page);
    await page.goto(`${productsPath}/new`);
    await fillTheForm(page, name, '4500', '0');

    await expect(page.getByRole('listitem').filter({ hasText: name })).toContainText(/sin stock/i);
  });

  // US-07, criteria 1 and 2: the switch is independent of everything else on
  // the row, end to end against the real API.
  test('turns a product off and back on from the listing', async ({ page }) => {
    const name = aNewProductName();

    await logInAsTheAdministrator(page);
    await page.goto(`${productsPath}/new`);
    await fillTheForm(page, name, '4500', '20');
    const row = page.getByRole('listitem').filter({ hasText: name });
    const nightly = row.getByRole('switch');

    await expect(nightly).toBeChecked();

    await nightly.click();

    await expect(nightly).not.toBeChecked();
    await expect(row).toContainText('No disponible');
    // The product is still there, and still sells its usual stock: turning
    // it off is not the same thing as running out or taking it off the menu.
    await expect(row).toContainText('20 en stock');

    await nightly.click();

    await expect(nightly).toBeChecked();
    await expect(row).toContainText('Disponible');
  });

  // Running out locks the switch. The drink comes back by being restocked,
  // which is US-08, and not by anybody tapping this.
  test('locks the nightly switch of a product that has no stock', async ({ page }) => {
    const name = aNewProductName();

    await logInAsTheAdministrator(page);
    await page.goto(`${productsPath}/new`);
    await fillTheForm(page, name, '4500', '0');
    const row = page.getByRole('listitem').filter({ hasText: name });

    await expect(row.getByRole('switch')).not.toBeChecked();
    await expect(row.getByRole('switch')).toBeDisabled();
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
