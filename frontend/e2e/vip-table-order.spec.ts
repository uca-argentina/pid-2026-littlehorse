import { expect, test } from '@playwright/test';
import type { APIRequestContext } from '@playwright/test';
import { categoryIdNamed } from './categories';
import { joinTonight, tonightsNight } from './nights';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';
import { loadIntoTonight } from './stock';

/**
 * US-41, end to end against the real API: the QR of a VIP table opens its
 * menu with the balance, and an order from it is paid from that balance and
 * carried to the table.
 */
interface StaffBody {
  readonly id: string;
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
  readonly spent: number;
  readonly accessCode: string;
}

async function adminToken(request: APIRequestContext): Promise<string> {
  const login = await request.post(`/api/${seededVenueSlug}/auth/login`, {
    data: { username: seededAdminUsername, password: seededAdminPassword() },
  });
  expect(login.ok()).toBe(true);

  return ((await login.json()) as { token: string }).token;
}

/** A night selling tables needs a waiter in its crew. */
async function aWaiterTonight(request: APIRequestContext, token: string): Promise<void> {
  const headers = { Authorization: `Bearer ${token}` };
  const staff = (await (await request.get('/api/staff/users', { headers })).json()) as StaffBody[];
  let waiter = staff.find((user) => user.role === 'Waiter' && user.isActive)?.id;

  if (waiter === undefined) {
    const created = await request.post('/api/staff/users', {
      headers,
      data: {
        username: `e2e.waiter${Date.now().toString(36)}`,
        password: 'a-long-enough-password',
        role: 'Waiter',
      },
    });
    expect(created.status()).toBe(201);
    waiter = ((await created.json()) as StaffBody).id;
  }

  await joinTonight(request, token, waiter);
}

/** A table of its own, sold tonight with that budget. Returns its number and code. */
async function aTableSoldTonight(
  request: APIRequestContext,
  token: string,
  budget: number,
): Promise<{ number: number; code: string; tableId: string; nightId: string }> {
  const headers = { Authorization: `Bearer ${token}` };
  const taken = new Set(
    ((await (await request.get('/api/staff/tables', { headers })).json()) as TableBody[]).map(
      (table) => table.number,
    ),
  );
  const number = [...Array(999).keys()].map((n) => n + 1).find((n) => !taken.has(n))!;
  const created = await request.post('/api/staff/tables', {
    headers,
    data: { number, capacity: 8, location: null },
  });
  expect(created.status()).toBe(201);
  const tableId = ((await created.json()) as TableBody).id;

  const tonight = await tonightsNight(request, headers);
  const sold = (await (
    await request.get(`/api/nights/${tonight.id}/tables`, { headers })
  ).json()) as NightTableBody[];
  const saved = await request.put(`/api/nights/${tonight.id}`, {
    headers,
    data: {
      ...tonight,
      tables: [
        ...sold.map((table) => ({ tableId: table.tableId, budget: table.budget })),
        { tableId, budget },
      ],
    },
  });
  expect(saved.ok()).toBe(true);

  const after = (await (
    await request.get(`/api/nights/${tonight.id}/tables`, { headers })
  ).json()) as NightTableBody[];

  return {
    number,
    tableId,
    nightId: tonight.id,
    code: after.find((table) => table.tableId === tableId)!.accessCode,
  };
}

async function aDrinkOnTheMenu(request: APIRequestContext, token: string): Promise<string> {
  const name = `E2E ${Date.now().toString(36)}${Math.random().toString(36).slice(2, 5)}`;
  const created = await request.post('/api/staff/products', {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      name,
      description: 'Cargado por la prueba',
      price: 4500,
      stock: 10,
      categoryId: await categoryIdNamed(request, token, 'Tragos'),
    },
  });
  expect(created.status()).toBe(201);
  await loadIntoTonight(request, token, ((await created.json()) as { id: string }).id, 10);

  return name;
}

test.describe('Ordering from a VIP table', () => {
  test('opens the table from its QR and pays from its balance', async ({ page, request }) => {
    const token = await adminToken(request);
    await aWaiterTonight(request, token);
    const table = await aTableSoldTonight(request, token, 50000);
    const drink = await aDrinkOnTheMenu(request, token);

    // The QR the waiter hands over lands on the table's menu.
    await page.goto(`/${seededVenueSlug}/table/${table.code}`);
    await expect(page).toHaveURL(new RegExp(`/${seededVenueSlug}/menu$`));
    const yourTable = page.getByRole('status', { name: /tu mesa/i });
    await expect(yourTable).toContainText(`Mesa ${table.number}`);
    await expect(yourTable).toContainText('$ 50.000,00');

    await page.getByRole('searchbox', { name: /buscar trago/i }).fill(drink);
    await page.getByRole('button', { name: new RegExp(`agregar ${drink}`, 'i') }).click();
    await page.getByTestId('order-summary').click();
    await page.getByRole('link', { name: /ir a pagar/i }).click();

    // Nothing to choose: the table's balance pays.
    await expect(page.getByText(/disponible \$ 50\.000,00/i)).toBeVisible();
    await expect(page.getByLabel(/pago digital/i)).toHaveCount(0);
    await page.getByRole('textbox', { name: /nombre/i }).fill('María Quadro');
    await page.getByRole('button', { name: /pagar/i }).click();

    await expect(
      page.getByText(new RegExp(`te lo llevamos a la mesa ${table.number}`, 'i')),
    ).toBeVisible({
      timeout: 15000,
    });

    const after = (await (
      await request.get(`/api/nights/${table.nightId}/tables`, {
        headers: { Authorization: `Bearer ${token}` },
      })
    ).json()) as NightTableBody[];
    expect(after.find((sold) => sold.tableId === table.tableId)!.spent).toBe(4500);
  });

  // US-42: the balance falls short, the screen splits the order, and the phone
  // pays the difference. The balance ends at zero.
  test('pays from the phone what the balance does not cover', async ({ page, request }) => {
    const token = await adminToken(request);
    await aWaiterTonight(request, token);
    const table = await aTableSoldTonight(request, token, 3000);
    const drink = await aDrinkOnTheMenu(request, token);

    await page.goto(`/${seededVenueSlug}/table/${table.code}`);
    await expect(page).toHaveURL(new RegExp(`/${seededVenueSlug}/menu$`));
    await page.getByRole('searchbox', { name: /buscar trago/i }).fill(drink);
    await page.getByRole('button', { name: new RegExp(`agregar ${drink}`, 'i') }).click();
    await page.getByTestId('order-summary').click();
    await page.getByRole('link', { name: /ir a pagar/i }).click();

    // Criterion 1: what the balance covers and what is left to pay.
    const split = page.getByTestId('split');
    await expect(split).toContainText('$ 3.000,00');
    await expect(split).toContainText('$ 1.500,00');
    await page.getByRole('textbox', { name: /nombre/i }).fill('María Quadro');
    const pay = page.getByRole('button', { name: /pagar la diferencia/i });
    await expect(pay).toContainText('$ 1.500,00');
    await pay.click();

    // Criterion 2: the order is in, carried to the table, and the balance is at zero.
    await expect(
      page.getByText(new RegExp(`te lo llevamos a la mesa ${table.number}`, 'i')),
    ).toBeVisible({
      timeout: 15000,
    });

    const after = (await (
      await request.get(`/api/nights/${table.nightId}/tables`, {
        headers: { Authorization: `Bearer ${token}` },
      })
    ).json()) as NightTableBody[];
    const sold = after.find((row) => row.tableId === table.tableId)!;
    expect(sold.budget - sold.spent).toBe(0);
  });

  // Criterion 4: a code nobody was given.
  test('says a code that opens nothing no longer works, and offers to order for the bar', async ({
    page,
  }) => {
    await page.goto(`/${seededVenueSlug}/table/${'0'.repeat(32)}`);

    await expect(page.getByRole('alert')).toContainText('Este código ya no sirve');
    await page.getByRole('button', { name: /pedir para retirar en la barra/i }).click();
    await expect(page).toHaveURL(new RegExp(`/${seededVenueSlug}/menu$`));
  });
});
