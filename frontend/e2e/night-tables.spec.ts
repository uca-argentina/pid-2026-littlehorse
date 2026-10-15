import { expect, test } from '@playwright/test';
import type { APIRequestContext, Page } from '@playwright/test';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';

/**
 * US-40, end to end against the real API: a night is set up with a VIP table
 * and the budget of its package, opened again with them, and changed.
 */
interface StaffBody {
  readonly id: string;
  readonly username: string;
  readonly role: string;
  readonly isActive: boolean;
}

interface TableBody {
  readonly id: string;
  readonly number: number;
}

interface NightTableBody {
  readonly tableId: string;
  readonly budget: number;
}

const DAY = 24 * 60 * 60 * 1000;

async function adminHeaders(request: APIRequestContext): Promise<Record<string, string>> {
  const login = await request.post(`/api/${seededVenueSlug}/auth/login`, {
    data: { username: seededAdminUsername, password: seededAdminPassword() },
  });
  expect(login.ok()).toBe(true);

  return { Authorization: `Bearer ${((await login.json()) as { token: string }).token}` };
}

/** An active account of that role, made for this purpose when the venue has none. */
async function anAccount(
  request: APIRequestContext,
  headers: Record<string, string>,
  role: 'Kds' | 'Cashier' | 'Waiter',
): Promise<string> {
  const staff = (await (await request.get('/api/staff/users', { headers })).json()) as StaffBody[];
  const existing = staff.find((user) => user.role === role && user.isActive);

  if (existing) return existing.username;

  const username = `e2e.${role.toLowerCase()}${Date.now().toString(36)}`;
  const created = await request.post('/api/staff/users', {
    headers,
    data: { username, password: 'a-long-enough-password', role },
  });
  expect(created.status()).toBe(201);

  return username;
}

/**
 * A table of its own for this run. The development database keeps every run's
 * tables, and a number is unique per venue, so it takes one nobody has.
 */
async function aFreshTable(
  request: APIRequestContext,
  headers: Record<string, string>,
): Promise<number> {
  const taken = new Set(
    ((await (await request.get('/api/staff/tables', { headers })).json()) as TableBody[]).map(
      (table) => table.number,
    ),
  );
  const number = [...Array(999).keys()].map((n) => n + 1).find((n) => !taken.has(n));

  if (number === undefined)
    throw new Error('No free table number left in the development database.');

  const created = await request.post('/api/staff/tables', {
    headers,
    data: { number, capacity: 10, location: 'Al lado de la cabina' },
  });
  expect(created.status()).toBe(201);

  return number;
}

/**
 * "2031-05-10T23:00", a day nobody uses: nights of a venue never overlap, and
 * the development database keeps every run's. Years ahead, so tonight's
 * night and the real ones are left alone.
 */
function aFreeEvening(): { starts: string; ends: string } {
  const day = new Date(Date.now() + (1500 + Math.floor(Math.random() * 3000)) * DAY);
  const pad = (value: number) => String(value).padStart(2, '0');
  const date = `${day.getFullYear()}-${pad(day.getMonth() + 1)}-${pad(day.getDate())}`;
  const next = new Date(day.getTime() + DAY);
  const nextDate = `${next.getFullYear()}-${pad(next.getMonth() + 1)}-${pad(next.getDate())}`;

  return { starts: `${date}T23:00`, ends: `${nextDate}T06:00` };
}

async function logInAsTheAdministrator(page: Page): Promise<void> {
  await page.goto(`/${seededVenueSlug}/staff/login`);
  await page.getByRole('textbox', { name: /usuario/i }).fill(seededAdminUsername);
  await page.getByRole('textbox', { name: /contraseña/i }).fill(seededAdminPassword());
  await page.getByRole('button', { name: /entrar/i }).click();
  await expect(page).toHaveURL(/\/staff\/products$/);
}

async function pick(page: Page, group: RegExp, username: string): Promise<void> {
  await page.getByRole('button', { name: group }).click();
  await page.getByRole('checkbox', { name: username, exact: true }).check();
  await page.getByRole('button', { name: 'Listo' }).click();
}

test.describe('VIP tables of a night', () => {
  test('sets up a night with a table and its budget, and changes it later', async ({
    page,
    request,
  }) => {
    const headers = await adminHeaders(request);
    const [kds, cashier, waiter] = [
      await anAccount(request, headers, 'Kds'),
      await anAccount(request, headers, 'Cashier'),
      await anAccount(request, headers, 'Waiter'),
    ];
    const number = await aFreshTable(request, headers);
    const evening = aFreeEvening();

    await logInAsTheAdministrator(page);
    await page.goto(`/${seededVenueSlug}/staff/nights/new`);

    await page.getByLabel('Nombre').fill('E2E con mesas');
    await page.getByLabel('Empieza').fill(evening.starts);
    await page.getByLabel('Termina').fill(evening.ends);
    await pick(page, /^KDS/, kds);
    await pick(page, /^Cajeros/, cashier);
    await pick(page, /^Mozos/, waiter);
    await page.getByRole('checkbox', { name: `Mesa ${number}`, exact: true }).check();
    await page.getByLabel(`Presupuesto de la mesa ${number}`).fill('150.000');
    await page.getByRole('button', { name: /crear noche/i }).click();

    // Creating lands on the night's own screen, where the table is with its budget.
    await expect(page).toHaveURL(/\/staff\/nights\/[0-9a-f-]+$/);
    const nightId = page.url().split('/').pop()!;
    await expect(page.getByRole('checkbox', { name: `Mesa ${number}`, exact: true })).toBeChecked();
    await expect(page.getByLabel(`Presupuesto de la mesa ${number}`)).toHaveValue('150000');

    await page.getByLabel(`Presupuesto de la mesa ${number}`).fill('200000');
    await page.getByRole('button', { name: /guardar cambios/i }).click();
    await expect(page).toHaveURL(new RegExp(`/${seededVenueSlug}/staff/nights$`));

    const tonight = (await (
      await request.get(`/api/nights/${nightId}/tables`, { headers })
    ).json()) as NightTableBody[];
    expect(tonight.map((table) => table.budget)).toEqual([200000]);
  });
});
