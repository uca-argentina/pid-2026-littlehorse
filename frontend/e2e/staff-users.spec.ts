import type { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from '@playwright/test';
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
  await expect(page).toHaveURL(new RegExp(`${staffAreaPath}/products$`));
}

async function createStaffUser(page: Page, username: string, role: RegExp): Promise<void> {
  await page.goto(`${staffUsersPath}/new`);
  await page.getByRole('textbox', { name: /usuario/i }).fill(username);
  await page.getByLabel('Contraseña', { exact: true }).fill(aNewPassword);
  await page.getByRole('radio', { name: role }).check();
  await page.getByRole('button', { name: /crear usuario/i }).click();
}

/**
 * Salir lives behind the account chip in the administration header: open the
 * menu, then leave.
 */
async function signOut(page: Page): Promise<void> {
  await page.getByRole('button', { name: new RegExp(`^${seededAdminUsername} · `, 'i') }).click();
  await page.getByRole('menuitem', { name: /salir/i }).click();
}

/**
 * Somebody's id, looked up as the administrator. A non-administrator cannot ask
 * the API who they are — that is the whole point of the test that uses this —
 * so the id has to come from an account that can.
 */
async function idOf(request: APIRequestContext, username: string): Promise<string> {
  const administrator = await tokenFor(request, seededAdminUsername, seededAdminPassword());
  const listing = await request.get('/api/staff/users', {
    headers: { Authorization: `Bearer ${administrator}` },
  });

  const everyone = (await listing.json()) as { id: string; username: string }[];
  const found = everyone.find((user) => user.username === username);

  expect(found, `${username} is not in the listing`).toBeDefined();

  return found!.id;
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

    // The way in is the tab in the header, not a typed address: an
    // administrator who has to be told the URL has no administration screen
    // at all.
    await page.getByRole('link', { name: /staff/i }).click();
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
    await signOut(page);
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

  /**
   * US-05, criteria 1 and 2 and then 4, end to end: access goes away, the row
   * stays, and the same account comes back. The half that matters is the login
   * attempt in the middle — a screen that says "dado de baja" proves nothing
   * about whether that person can still get in.
   */
  test('takes access away, keeps the row, and gives the account back', async ({ page }) => {
    const username = aNewUsername();

    await logInAsTheAdministrator(page);
    await createStaffUser(page, username, /KDS/i);
    await page.getByRole('searchbox', { name: /buscar usuario/i }).fill(username);
    await page.getByRole('link', { name: /editar/i }).click();

    await expect(page.getByRole('heading', { level: 1 })).toHaveText(username);
    await page.getByRole('button', { name: /dar de baja/i }).click();
    await page.getByRole('button', { name: /confirmar la baja/i }).click();
    await expect(page.getByRole('button', { name: /reactivar/i })).toBeVisible();

    // Criterion 2: still there, marked, not deleted.
    await page.getByRole('link', { name: /volver al equipo/i }).click();
    await page.getByRole('searchbox', { name: /buscar usuario/i }).fill(username);
    await expect(page.getByText(username, { exact: true })).toBeVisible();
    await expect(page.getByText(/dado de baja/i).first()).toBeVisible();

    // Criterion 1: the password is still right, and it still does not work.
    await signOut(page);
    await logIn(page, username, aNewPassword);

    await expect(page.getByRole('alert')).toHaveText(/usuario o contraseña incorrectos/i);
    await expect(page).toHaveURL(new RegExp(`${loginPath}$`));

    // Criterion 4: the account they always had, without loading them again.
    await logInAsTheAdministrator(page);
    await page.goto(staffUsersPath);
    await page.getByRole('searchbox', { name: /buscar usuario/i }).fill(username);
    await page.getByRole('link', { name: /editar/i }).click();
    await page.getByRole('button', { name: /reactivar/i }).click();
    await expect(page.getByRole('button', { name: /dar de baja/i })).toBeVisible();

    await signOut(page);
    await logIn(page, username, aNewPassword);

    await expect(page).toHaveURL(new RegExp(`${staffAreaPath}$`));
  });

  // US-04, criteria 2 and 3.
  test('corrects a role that was assigned wrong, and hands out a new password', async ({
    page,
  }) => {
    const username = aNewUsername();
    const anotherPassword = 'another-long-enough-password';

    await logInAsTheAdministrator(page);
    await createStaffUser(page, username, /mozo/i);
    await page.getByRole('searchbox', { name: /buscar usuario/i }).fill(username);
    await page.getByRole('link', { name: /editar/i }).click();

    await page.getByRole('radio', { name: /KDS/i }).check();
    await page.getByRole('button', { name: /guardar el rol/i }).click();
    await expect(page.getByText(/rol guardado/i)).toBeVisible();

    await page.getByLabel(/contraseña nueva/i).fill(anotherPassword);
    await page.getByRole('button', { name: /cambiar la contraseña/i }).click();
    await expect(page.getByText(/contraseña cambiada/i)).toBeVisible();

    await signOut(page);

    // The old one stopped working the moment the new one was saved.
    await logIn(page, username, aNewPassword);
    await expect(page.getByRole('alert')).toHaveText(/usuario o contraseña incorrectos/i);

    // The new one works, and the role it lands on is the corrected one.
    await logIn(page, username, anotherPassword);
    await expect(page).toHaveURL(new RegExp(`${staffAreaPath}$`));
    await expect(page.getByText(/KDS · estación de barra/)).toBeVisible();
  });

  test.describe('with an account that is not an administrator', () => {
    // Criterion 6, the half the person sees: the screen never opens.
    test('typing the administration address gets nowhere', async ({ page }) => {
      const username = aNewUsername();

      await logInAsTheAdministrator(page);
      await createStaffUser(page, username, /KDS/i);
      // Waited for before signing out: the form has a way out in its header
      // too, and leaving while the request is in flight cancels it.
      await expect(page.getByText(username, { exact: true })).toBeVisible();
      await signOut(page);

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
    /**
     * Every administration endpoint, not just the listing. A guard in the PWA
     * only hides a screen: anybody can call the API without one, so this is
     * where "only an administrator" is actually decided.
     *
     * Their own account included. A KDS cannot change their own role, reset
     * their own password or deactivate themselves either — the whole group is
     * for administrators, and being the subject of the request changes nothing.
     */
    test('the API refuses every administration endpoint, their own account included', async ({
      page,
      request,
    }) => {
      const username = aNewUsername();

      await logInAsTheAdministrator(page);
      await createStaffUser(page, username, /KDS/i);
      await expect(page.getByText(username, { exact: true })).toBeVisible();

      const token = await tokenFor(request, username, aNewPassword);
      const asThemselves = { headers: { Authorization: `Bearer ${token}` } };
      const theirOwnId = await idOf(request, username);

      const refused = [
        await request.get('/api/staff/users', asThemselves),
        await request.post('/api/staff/users', {
          ...asThemselves,
          data: { username: aNewUsername(), password: aNewPassword, role: 'Waiter' },
        }),
        await request.put(`/api/staff/users/${theirOwnId}/role`, {
          ...asThemselves,
          data: { role: 'Administrator' },
        }),
        await request.put(`/api/staff/users/${theirOwnId}/password`, {
          ...asThemselves,
          data: { password: 'another-long-enough-one' },
        }),
        await request.post(`/api/staff/users/${theirOwnId}/deactivate`, asThemselves),
        await request.post(`/api/staff/users/${theirOwnId}/reactivate`, asThemselves),
      ];

      expect(refused.map((response) => response.status())).toEqual([403, 403, 403, 403, 403, 403]);
    });
  });
});
