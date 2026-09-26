import { expect, test } from '@playwright/test';
import type { Page } from '@playwright/test';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';

/**
 * US-14, the part that lets the venue name its own categories, end to end
 * against the real API and the seeded database: an administrator adds one from
 * the products screen, loads a product under it, and the customer finds it as a
 * tab of the menu.
 */
const loginPath = `/${seededVenueSlug}/staff/login`;
const productsPath = `/${seededVenueSlug}/staff/products`;

/**
 * The development database is not reset between runs, and a name is unique per
 * venue: reusing a fixed one makes the second run fail on a conflict that has
 * nothing to do with the change being tested. Short, because a category name
 * is limited to forty characters.
 */
function aNewName(prefix: string): string {
  return `${prefix} ${Date.now().toString(36)}${Math.random().toString(36).slice(2, 5)}`;
}

async function logInAsTheAdministrator(page: Page): Promise<void> {
  await page.goto(loginPath);
  await page.getByRole('textbox', { name: /usuario/i }).fill(seededAdminUsername);
  await page.getByRole('textbox', { name: /contraseña/i }).fill(seededAdminPassword());
  await page.getByRole('button', { name: /entrar/i }).click();
  await expect(page).toHaveURL(new RegExp(`${productsPath}$`));
}

async function addACategory(page: Page, name: string): Promise<void> {
  await page.getByRole('link', { name: /nueva categoría/i }).click();

  const field = page.getByRole('textbox', { name: /^nombre/i });

  // The input is in the page a moment before Angular binds it to its control,
  // and on a second visit, with the screen already cached, a fill lands in that
  // gap and is lost. The class is what tells the binding is done; nobody types
  // that fast, so this is a wait of the test and not a bug of the screen.
  await expect(field).toHaveClass(/ng-pristine/);
  await field.fill(name);
  await page.getByRole('button', { name: /crear categoría/i }).click();
}

test.describe('Categories', () => {
  test('adds a category that then shows up in a product form and on the customer menu', async ({
    page,
  }) => {
    const category = aNewName('Cat');
    const product = aNewName('E2E');

    await logInAsTheAdministrator(page);
    await addACategory(page, category);

    // Saving lands back on the products, where the button that led there is.
    await expect(page).toHaveURL(new RegExp(`${productsPath}$`));

    await page.getByRole('link', { name: /nuevo producto/i }).click();
    await expect(page.getByRole('radio', { name: category })).toBeVisible();
    await page.getByRole('textbox', { name: /^nombre/i }).fill(product);
    await page.getByRole('radio', { name: category }).check();
    await page.getByRole('spinbutton', { name: /precio/i }).fill('4500');
    await page.getByRole('spinbutton', { name: /stock/i }).fill('10');
    await page.getByRole('button', { name: /crear producto/i }).click();
    await expect(page).toHaveURL(new RegExp(`${productsPath}$`));

    await page.goto(`/${seededVenueSlug}/menu`);
    await page.getByRole('tab', { name: category }).click();

    await expect(page.getByRole('listitem').filter({ hasText: product })).toBeVisible();
  });

  test('refuses a name that this venue already has, and says why', async ({ page }) => {
    const category = aNewName('Cat');

    await logInAsTheAdministrator(page);
    await addACategory(page, category);
    await expect(page).toHaveURL(new RegExp(`${productsPath}$`));

    await addACategory(page, category.toLowerCase());

    await expect(page.getByRole('alert')).toContainText(/ya hay una categoría con ese nombre/i);
    await expect(page).toHaveURL(new RegExp(`/${seededVenueSlug}/staff/categories/new$`));
  });

  test('does not save without a name', async ({ page }) => {
    await logInAsTheAdministrator(page);

    await addACategory(page, '   ');

    await expect(page.getByText(/ponele un nombre/i)).toBeVisible();
    await expect(page).toHaveURL(new RegExp(`/${seededVenueSlug}/staff/categories/new$`));
  });
});
