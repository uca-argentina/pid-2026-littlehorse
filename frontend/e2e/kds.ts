import { expect } from '@playwright/test';
import type { APIRequestContext, Page } from '@playwright/test';
import { categoryIdNamed } from './categories';
import { joinTonight } from './nights';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';

/**
 * What the bar's specs set up before they start: a station account, a drink
 * on the menu, and an order a customer paid for. Shared by the board's spec
 * and the scan screen's, so both build their data the same way.
 */
export const loginPath = `/${seededVenueSlug}/staff/login`;
export const kdsPath = `/${seededVenueSlug}/staff/kds`;
export const kdsScanPath = `/${seededVenueSlug}/staff/kds/scan`;
const menuPath = `/${seededVenueSlug}/menu`;

/** Unique per run: the development database keeps everything every run created. */
function aNewUsername(): string {
  return `e2e.${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
}

function aNewProduct(): string {
  return `E2E ${Date.now().toString(36)}${Math.random().toString(36).slice(2, 5)}`;
}

/**
 * Letters only, in two words: the checkout screen's own full-name pattern
 * (FULL_NAME in checkout.page.ts) refuses a name with a digit in it, which is
 * what every other "unique per run" helper here produces.
 */
export function aFullName(): string {
  const word = () =>
    Array.from({ length: 8 }, () => String.fromCharCode(97 + Math.floor(Math.random() * 26))).join(
      '',
    );

  return `Cliente ${word()}`;
}

export const aNewPassword = 'a-long-enough-password';

async function adminToken(request: APIRequestContext): Promise<string> {
  const login = await request.post(`/api/${seededVenueSlug}/auth/login`, {
    data: { username: seededAdminUsername, password: seededAdminPassword() },
  });

  return ((await login.json()) as { token: string }).token;
}

/** A fresh Kds account, the way an administrator would create one, working tonight. */
export async function aKdsAccount(request: APIRequestContext): Promise<string> {
  const username = aNewUsername();

  const token = await adminToken(request);
  const created = await request.post('/api/staff/users', {
    headers: { Authorization: `Bearer ${token}` },
    data: { username, password: aNewPassword, role: 'Kds' },
  });

  expect(created.status()).toBe(201);
  await joinTonight(request, token, ((await created.json()) as { id: string }).id);

  return username;
}

/** A fresh Kds account that nobody added to tonight's night (US-35, criterion 4). */
export async function aKdsAccountOffTonight(request: APIRequestContext): Promise<string> {
  const username = aNewUsername();

  const created = await request.post('/api/staff/users', {
    headers: { Authorization: `Bearer ${await adminToken(request)}` },
    data: { username, password: aNewPassword, role: 'Kds' },
  });

  expect(created.status()).toBe(201);

  return username;
}

async function loadProduct(request: APIRequestContext, name: string): Promise<void> {
  const token = await adminToken(request);

  const created = await request.post('/api/staff/products', {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      name,
      description: 'Cargado por la prueba',
      price: 4500,
      stock: 20,
      categoryId: await categoryIdNamed(request, token, 'Tragos'),
    },
  });

  expect(created.status()).toBe(201);
}

export async function logIn(page: Page, username: string, password: string): Promise<void> {
  await page.goto(loginPath);
  await page.getByRole('textbox', { name: /usuario/i }).fill(username);
  await page.getByRole('textbox', { name: /contraseña/i }).fill(password);
  await page.getByRole('button', { name: /entrar/i }).click();
}

/** Places and pays for one drink as a customer, digital — the only method there is today. */
export async function anOrderPaidBy(
  page: Page,
  request: APIRequestContext,
  customerName: string,
): Promise<string> {
  const drinkName = aNewProduct();
  await loadProduct(request, drinkName);

  await page.goto(menuPath);
  await page.getByRole('searchbox', { name: /buscar trago/i }).fill(drinkName);
  await page.getByRole('button', { name: new RegExp(`agregar ${drinkName}`, 'i') }).click();
  await page.getByTestId('order-summary').click();
  await page.getByRole('link', { name: /ir a pagar/i }).click();

  await page.getByRole('textbox', { name: /nombre/i }).fill(customerName);
  await page.getByRole('button', { name: /pagar/i }).click();
  await expect(page.getByTestId('order-code')).toBeVisible({ timeout: 15000 });

  return drinkName;
}
