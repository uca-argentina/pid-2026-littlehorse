import { expect, test } from '@playwright/test';
import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';

/**
 * US-39, end to end against the real API and the seeded database: an
 * administrator adds a VIP table, puts it away and brings it back, and a
 * number that is taken stays taken while the table is away.
 */
const loginPath = `/${seededVenueSlug}/staff/login`;
const tablesPath = `/${seededVenueSlug}/staff/tables`;

async function adminToken(request: APIRequestContext): Promise<string> {
  const login = await request.post(`/api/${seededVenueSlug}/auth/login`, {
    data: { username: seededAdminUsername, password: seededAdminPassword() },
  });
  expect(login.ok()).toBe(true);

  return ((await login.json()) as { token: string }).token;
}

/**
 * The development database is not reset between runs, and a table number is
 * unique per venue and limited to 999: a fixed one fails the second run on a
 * conflict that has nothing to do with the change being tested, so each test
 * takes a number that nobody has.
 */
async function aFreeNumber(request: APIRequestContext): Promise<number> {
  const listed = await request.get('/api/staff/tables', {
    headers: { Authorization: `Bearer ${await adminToken(request)}` },
  });
  const taken = new Set(((await listed.json()) as { number: number }[]).map((t) => t.number));

  for (let attempt = 0; attempt < 50; attempt++) {
    const candidate = 1 + Math.floor(Math.random() * 999);
    if (!taken.has(candidate)) return candidate;
  }

  throw new Error('The development database has no free table number left to test with.');
}

async function logInAsTheAdministrator(page: Page): Promise<void> {
  await page.goto(loginPath);
  await page.getByRole('textbox', { name: /usuario/i }).fill(seededAdminUsername);
  await page.getByRole('textbox', { name: /contraseña/i }).fill(seededAdminPassword());
  await page.getByRole('button', { name: /entrar/i }).click();
  await expect(page).toHaveURL(/\/staff\/products$/);
}

async function addATable(page: Page, number: number): Promise<void> {
  await page.getByRole('link', { name: /nueva mesa/i }).click();

  const field = page.getByRole('textbox', { name: /^número/i });

  // The input is in the page a moment before Angular binds it to its control,
  // and a fill that lands in that gap is lost. The class is what tells the
  // binding is done; nobody types that fast, so this is a wait of the test.
  await expect(field).toHaveClass(/ng-pristine/);
  await field.fill(`${number}`);
  await page.getByRole('button', { name: /crear mesa/i }).click();
}

/** The row of one table, not of 5 when looking for 55. */
function rowOf(page: Page, number: number): Locator {
  return page.getByRole('listitem').filter({ hasText: new RegExp(`Mesa ${number}(?!\\d)`) });
}

test.describe('VIP tables', () => {
  test('adds a table, puts it away and brings it back', async ({ page, request }) => {
    const number = await aFreeNumber(request);

    await logInAsTheAdministrator(page);
    await page.goto(tablesPath);
    await addATable(page, number);

    // Saving lands back on the listing, where the new table is.
    await expect(page).toHaveURL(new RegExp(`${tablesPath}$`));
    await expect(rowOf(page, number)).toBeVisible();

    await page
      .getByRole('button', { name: new RegExp(`dar de baja la mesa ${number}$`, 'i') })
      .click();
    await expect(rowOf(page, number)).toContainText('Dada de baja');

    await page
      .getByRole('button', { name: new RegExp(`reactivar la mesa ${number}$`, 'i') })
      .click();
    await expect(rowOf(page, number)).not.toContainText('Dada de baja');
  });

  // The number of a table that is away is still taken: its old orders say
  // "Mesa N", and a new one with that number would make them ambiguous.
  test('refuses a number that is taken, even by a table that is put away', async ({
    page,
    request,
  }) => {
    const number = await aFreeNumber(request);

    await logInAsTheAdministrator(page);
    await page.goto(tablesPath);
    await addATable(page, number);
    await expect(rowOf(page, number)).toBeVisible();
    await page
      .getByRole('button', { name: new RegExp(`dar de baja la mesa ${number}$`, 'i') })
      .click();
    await expect(rowOf(page, number)).toContainText('Dada de baja');

    await addATable(page, number);

    await expect(page.getByRole('alert')).toContainText(/ya hay una mesa con ese número/i);
    await expect(page).toHaveURL(new RegExp(`${tablesPath}/new$`));
  });

  test('says what a number has to be before sending anything', async ({ page }) => {
    await logInAsTheAdministrator(page);
    await page.goto(tablesPath);

    await addATable(page, 1000);

    await expect(page.getByText(/entre 1 y 999/i).first()).toBeVisible();
    await expect(page).toHaveURL(new RegExp(`${tablesPath}/new$`));
  });
});
