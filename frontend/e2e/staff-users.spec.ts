import { expect, test } from '@playwright/test';
import type { APIRequestContext, Page } from '@playwright/test';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';

/**
 * US-03, end to end against the real API and the seeded database: an
 * administrator adds somebody, that person shows up in the listing and signs in
 * straight away, and nobody else reaches the administration screens.
 */
const loginPath = `/${seededVenueSlug}/staff/login`;
const staffAreaPath = `/${seededVenueSlug}/staff`;
const staffUsersPath = `/${seededVenueSlug}/staff/users`;

/**
 * The development database is not reset between runs, and a username is unique
 * per venue: reusing a fixed one makes the second run fail on a conflict that
 * has nothing to do with the change being tested.
 */
function aNewUsername(): string {
  return `e2e.${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
}

const aNewPassword = 'a-long-enough-password';

async function logIn(page: Page, username: string, password: string): Promise<void> {
  await page.goto(loginPath);
  await page.getByRole('textbox', { name: /usuario/i }).fill(username);
  await page.getByRole('textbox', { name: /contraseña/i }).fill(password);
  await page.getByRole('button', { name: /entrar/i }).click();
}

async function logInAsTheAdministrator(page: Page): Promise<void> {
  await logIn(page, seededAdminUsername, seededAdminPassword());
  await expect(page).toHaveURL(new RegExp(`${staffAreaPath}$`));
}

async function createStaffUser(page: Page, username: string, role: RegExp): Promise<void> {
  await page.goto(`${staffUsersPath}/new`);
  await page.getByRole('textbox', { name: /usuario/i }).fill(username);
  await page.getByLabel('Contraseña', { exact: true }).fill(aNewPassword);
  await page.getByRole('radio', { name: role }).check();
  await page.getByRole('button', { name: /crear usuario/i }).click();
}

/** A token straight from the API, to ask it things the screens will not ask. */
async function tokenFor(
  request: APIRequestContext,
  username: string,
  password: string,
): Promise<string> {
  const response = await request.post(`/api/${seededVenueSlug}/auth/login`, {
    data: { username, password },
  });

  expect(response.status()).toBe(200);

  return ((await response.json()) as { token: string }).token;
}

test.describe('Staff users', () => {
  test('adds somebody who then shows up in the listing and can sign in', async ({ page }) => {
    const username = aNewUsername();

    await logInAsTheAdministrator(page);

    // The way in is the home screen, not a typed address: an administrator who
    // has to be told the URL has no administration screen at all.
    await page.getByRole('link', { name: /usuarios internos/i }).click();
    await expect(page).toHaveURL(new RegExp(`${staffUsersPath}$`));

    await page.getByRole('link', { name: /nuevo usuario/i }).click();
    await page.getByRole('textbox', { name: /usuario/i }).fill(username);
    await page.getByLabel('Contraseña', { exact: true }).fill(aNewPassword);
    await page.getByRole('radio', { name: /KDS/i }).check();
    await page.getByRole('button', { name: /crear usuario/i }).click();

    // Criterion 1, first half: they appear in the listing.
    await expect(page).toHaveURL(new RegExp(`${staffUsersPath}$`));
    await expect(page.getByText(username, { exact: true })).toBeVisible();

    // Criterion 1, second half: they can sign in straight away. This is the one
    // that catches a user created inactive, or with the password stored wrong.
    await page.getByRole('button', { name: /salir/i }).click();
    await logIn(page, username, aNewPassword);

    await expect(page).toHaveURL(new RegExp(`${staffAreaPath}$`));
    await expect(page.getByText(/KDS · estación de barra/)).toBeVisible();
  });

  // Criterion 3. The administrator has to be able to fix it on the spot, so the
  // message has to say what is wrong rather than that something failed.
  test('refuses a username that this venue already uses', async ({ page }) => {
    const username = aNewUsername();

    await logInAsTheAdministrator(page);
    await createStaffUser(page, username, /mozo/i);
    await expect(page.getByText(username, { exact: true })).toBeVisible();

    await createStaffUser(page, username, /mozo/i);

    await expect(page.getByRole('alert')).toHaveText(/ya hay alguien con ese usuario/i);
    await expect(page).toHaveURL(new RegExp(`${staffUsersPath}/new$`));
  });

  // Criterion 5. The form stops it, so the venue's connection is never part of
  // finding out that a field was left empty.
  test('does not save a username of fewer than three characters', async ({ page }) => {
    await logInAsTheAdministrator(page);

    await page.goto(`${staffUsersPath}/new`);
    await page.getByRole('textbox', { name: /usuario/i }).fill('eu');
    await page.getByLabel('Contraseña', { exact: true }).fill(aNewPassword);
    await page.getByRole('button', { name: /crear usuario/i }).click();

    await expect(page.getByText(/al menos tres caracteres/i)).toBeVisible();
    await expect(page).toHaveURL(new RegExp(`${staffUsersPath}/new$`));
  });

  /**
   * US-13. The development database keeps everything every run ever created, so
   * by now the listing is long — which is the condition this exists for, and the
   * one a fresh database would not reproduce.
   */
  test('finds one person among everyone else', async ({ page }) => {
    const username = aNewUsername();

    await logInAsTheAdministrator(page);
    await createStaffUser(page, username, /KDS/i);

    await page.getByRole('searchbox', { name: /buscar usuario/i }).fill(username);

    await expect(page.getByRole('listitem')).toHaveCount(1);
    await expect(page.getByText(username, { exact: true })).toBeVisible();

    // The count follows the search rather than the venue, so a tab never
    // promises rows it will not show.
    await expect(page.getByRole('button', { name: /^todos/i })).toContainText('Todos · 1');
    await expect(page.getByRole('button', { name: /^mozos/i })).toContainText('Mozos · 0');
  });

  test.describe('with an account that is not an administrator', () => {
    // Criterion 6, the half the person sees: the screen never opens.
    test('typing the administration address gets nowhere', async ({ page }) => {
      const username = aNewUsername();

      await logInAsTheAdministrator(page);
      await createStaffUser(page, username, /KDS/i);
      await page.getByRole('button', { name: /salir/i }).click();

      // Waited for on purpose: the session is only stored once the login lands,
      // and navigating before that turns this into a test of the wrong guard.
      await logIn(page, username, aNewPassword);
      await expect(page).toHaveURL(new RegExp(`${staffAreaPath}$`));

      await page.goto(staffUsersPath);

      // Back to their own screen, not to the login: their session is fine, and
      // signing in again would change nothing about their role.
      await expect(page).toHaveURL(new RegExp(`${staffAreaPath}$`));
      await expect(page.getByRole('heading', { name: /hola/i })).toBeVisible();
    });

    // Criterion 6, the half that matters: a guard only hides a screen, and
    // anyone can call the API without one.
    test('the API refuses the administration endpoints too', async ({ page, request }) => {
      const username = aNewUsername();

      await logInAsTheAdministrator(page);
      await createStaffUser(page, username, /KDS/i);
      await expect(page.getByText(username, { exact: true })).toBeVisible();

      const token = await tokenFor(request, username, aNewPassword);
      const listing = await request.get('/api/staff/users', {
        headers: { Authorization: `Bearer ${token}` },
      });

      expect(listing.status()).toBe(403);
    });
  });
});
