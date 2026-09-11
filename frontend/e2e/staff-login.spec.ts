import { expect, test } from '@playwright/test';
import type { Page } from '@playwright/test';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';

/**
 * The staff login, end to end against the real API and the seeded database.
 * Every assertion is on what the person at the venue sees, not on internals.
 */
const loginPath = `/${seededVenueSlug}/staff/login`;
const staffAreaPath = `/${seededVenueSlug}/staff`;

async function logIn(page: Page, password: string): Promise<void> {
  await page.getByRole('textbox', { name: /usuario/i }).fill(seededAdminUsername);
  await page.getByRole('textbox', { name: /contraseña/i }).fill(password);
  await page.getByRole('button', { name: /entrar/i }).click();
}

test.describe('Staff login', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto(loginPath);
    await expect(page.getByRole('heading', { name: /iniciá tu turno/i })).toBeVisible();
  });

  test('rejects a wrong password and keeps the person on the screen', async ({ page }) => {
    await logIn(page, 'not-the-seeded-password');

    await expect(page.getByRole('alert')).toHaveText(/usuario o contraseña incorrectos/i);
    await expect(page).toHaveURL(new RegExp(`${loginPath}$`));

    // The password is cleared and the username is not: retyping both on a
    // tablet, in the dark, is how a bartender ends up locked out of a shift.
    await expect(page.getByRole('textbox', { name: /usuario/i })).toHaveValue(seededAdminUsername);
    await expect(page.getByRole('textbox', { name: /contraseña/i })).toHaveValue('');
  });

  test('opens the staff area for the seeded administrator', async ({ page }) => {
    await logIn(page, seededAdminPassword());

    await expect(page).toHaveURL(new RegExp(`${staffAreaPath}$`));
    await expect(
      page.getByRole('heading', { name: new RegExp(`hola, ${seededAdminUsername}`, 'i') }),
    ).toBeVisible();

    // The role comes from the token, and the screen shows what it decoded.
    await expect(page.getByText(/administrador/i)).toBeVisible();
  });

  test('keeps the session open when the tablet is reloaded', async ({ page }) => {
    await logIn(page, seededAdminPassword());
    await expect(page).toHaveURL(new RegExp(`${staffAreaPath}$`));

    // A tablet behind the bar gets locked, dropped and reloaded all night.
    // Asking for the password again on every reload makes the app unusable.
    await page.reload();

    await expect(
      page.getByRole('heading', { name: new RegExp(`hola, ${seededAdminUsername}`, 'i') }),
    ).toBeVisible();
  });

  test('closes the session and blocks the staff area again when signing out', async ({ page }) => {
    await logIn(page, seededAdminPassword());
    await page.getByRole('button', { name: /salir/i }).click();

    await expect(page).toHaveURL(new RegExp(`${loginPath}$`));

    // Typing the address by hand is exactly what someone does with a tablet
    // that changed hands, so the guard has to turn them away.
    await page.goto(staffAreaPath);

    await expect(page).toHaveURL(new RegExp(`${loginPath}$`));
    await expect(page.getByRole('heading', { name: /iniciá tu turno/i })).toBeVisible();
  });
});
