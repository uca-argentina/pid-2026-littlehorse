import { devices, expect, test } from '@playwright/test';
import type { Browser, Page } from '@playwright/test';
import {
  aFullName,
  aKdsAccount,
  aNewPassword,
  anOrderPaidBy,
  kdsPath,
  kdsScanPath,
  logIn,
} from './kds';

/**
 * US-20, closing US-19, end to end: the customer's phone shows the QR, the
 * bar's tablet reads it, and each side sees what the other did — against the
 * real API, the real database and the real SignalR connection.
 *
 * The scan goes through the reader's field, typed and ended with Enter: that
 * is exactly what a reader plugged into the tablet does, and a headless
 * browser has no camera to point at a phone. Whether a real tablet reads a
 * real phone at its lowest brightness is criterion 2, and it is checked by
 * hand.
 */

/** The customer, on a phone-sized screen, in a browser of their own. */
async function aCustomerPhone(browser: Browser): Promise<Page> {
  const { viewport, userAgent, deviceScaleFactor, isMobile, hasTouch } = devices['iPhone 13'];
  const context = await browser.newContext({
    viewport,
    userAgent,
    deviceScaleFactor,
    isMobile,
    hasTouch,
  });

  return context.newPage();
}

/**
 * What the QR carries. Read from the address of the customer's own screen,
 * which is where the token lives; that the QR draws exactly this and nothing
 * else is proved by the tracking page's unit tests.
 */
function tokenShownBy(customer: Page): string {
  const token = new URL(customer.url()).pathname.split('/').at(-1);

  expect(token).toMatch(/^[0-9a-f]{32}$/);

  return token!;
}

/** A reader plugged into the tablet: types what it read and presses Enter. */
async function theReaderReads(tablet: Page, read: string): Promise<void> {
  const field = tablet.getByRole('textbox', { name: 'Lectura del lector' });

  await field.fill(read);
  await field.press('Enter');
}

function lastScan(tablet: Page) {
  return tablet.getByRole('status', { name: 'Último escaneo' });
}

async function toTheScanScreen(tablet: Page): Promise<void> {
  await tablet.getByRole('link', { name: 'Escanear o buscar' }).click();
  await expect(tablet).toHaveURL(new RegExp(`${kdsScanPath}$`));
}

async function toTheBoard(tablet: Page): Promise<void> {
  await tablet.getByRole('link', { name: 'Volver al tablero' }).click();
  await expect(tablet).toHaveURL(new RegExp(`${kdsPath}$`));
}

function column(tablet: Page, name: string, customerName: string) {
  return tablet.getByRole('group', { name }).getByRole('article').filter({ hasText: customerName });
}

test.describe('KDS scan screen', () => {
  test('hands an order over when its qr is scanned, and the customer sees it arrive', async ({
    page: tablet,
    browser,
    request,
  }) => {
    const username = await aKdsAccount(request);
    const customerName = aFullName();

    // The customer pays and has the QR on screen, next to the code.
    const customer = await aCustomerPhone(browser);
    await anOrderPaidBy(customer, request, customerName);
    const code = (await customer.getByTestId('order-code').textContent())!.trim();
    await expect(
      customer.getByRole('img', { name: `Código QR para retirar el pedido ${code}` }),
    ).toBeVisible();
    const token = tokenShownBy(customer);

    await logIn(tablet, username, aNewPassword);
    await expect(tablet).toHaveURL(new RegExp(`${kdsPath}$`));

    // US-19, criterion 3: shown too early, it is not handed over.
    await toTheScanScreen(tablet);
    await theReaderReads(tablet, token);
    await expect(lastScan(tablet)).toContainText('Todavía no está listo');
    await expect(lastScan(tablet)).toContainText(code);

    // The bar makes it, and the customer's screen says so on its own.
    await toTheBoard(tablet);
    await column(tablet, 'Nuevos', customerName)
      .getByRole('button', { name: /^Preparar / })
      .click();
    await column(tablet, 'En preparación', customerName)
      .getByRole('button', { name: /^Listo / })
      .click();
    await expect(customer.getByText(/está listo/i)).toBeVisible({ timeout: 15000 });

    // US-19, criterion 1: the QR hands it over.
    await toTheScanScreen(tablet);
    await theReaderReads(tablet, token);
    await expect(lastScan(tablet)).toContainText('Entregado');
    await expect(lastScan(tablet)).toContainText(`${code} · ${customerName}`);

    // US-20, criterion 3: on the customer's side the journey ends and the QR
    // is gone — it no longer claims anything.
    await expect(customer.getByRole('status')).toContainText(/entregado/i, { timeout: 15000 });
    await expect(customer.getByRole('img', { name: /Código QR/ })).toHaveCount(0);

    // Off the board as well: nothing left to hand over.
    await toTheBoard(tablet);
    await expect(column(tablet, 'Listos en la barra', customerName)).toHaveCount(0);

    // US-19, criterion 4: the same QR held up again — somebody claiming it twice.
    // Read again until the screen answers it: the same code within a few
    // seconds is taken as the camera seeing the same scan, on purpose.
    await toTheScanScreen(tablet);
    await theReaderReads(tablet, token);
    await expect(lastScan(tablet)).toContainText('Ya se entregó');

    const recent = tablet.getByRole('list', { name: 'Últimos escaneos' }).getByRole('listitem');
    await expect(recent.first()).toContainText(code);

    await customer.context().close();
  });

  test('warns when the same qr is held up twice at the counter', async ({
    page: tablet,
    browser,
    request,
  }) => {
    const username = await aKdsAccount(request);
    const customerName = aFullName();
    const customer = await aCustomerPhone(browser);
    await anOrderPaidBy(customer, request, customerName);
    const token = tokenShownBy(customer);

    await logIn(tablet, username, aNewPassword);
    await column(tablet, 'Nuevos', customerName)
      .getByRole('button', { name: /^Preparar / })
      .click();
    await column(tablet, 'En preparación', customerName)
      .getByRole('button', { name: /^Listo / })
      .click();
    await expect(column(tablet, 'Listos en la barra', customerName)).toBeVisible();

    await toTheScanScreen(tablet);
    await theReaderReads(tablet, token);
    await expect(lastScan(tablet)).toContainText('Entregado');

    // On the same screen, a moment later: read until the repeat window has passed.
    await expect(async () => {
      await theReaderReads(tablet, token);
      await expect(lastScan(tablet)).toContainText('Ya se entregó', { timeout: 500 });
    }).toPass({ timeout: 10_000 });

    await expect(
      tablet.getByRole('list', { name: 'Últimos escaneos' }).getByRole('listitem'),
    ).toHaveCount(2);

    await customer.context().close();
  });

  test('says it does not know a code that is no order of this venue', async ({
    page: tablet,
    request,
  }) => {
    const username = await aKdsAccount(request);

    await logIn(tablet, username, aNewPassword);
    await toTheScanScreen(tablet);
    await theReaderReads(tablet, '0123456789abcdef0123456789abcdef');

    await expect(lastScan(tablet)).toContainText('No reconocemos este código');
  });

  // US-18, criterion 3 and US-19, criterion 2: no QR to read — a scratched
  // screen, a dead battery — so the order is found by name or number.
  test('finds an order by hand, marks it ready, hands it over and undoes it', async ({
    page: tablet,
    browser,
    request,
  }) => {
    const username = await aKdsAccount(request);
    const customerName = aFullName();
    const customer = await aCustomerPhone(browser);
    await anOrderPaidBy(customer, request, customerName);
    const code = (await customer.getByTestId('order-code').textContent())!.trim();

    await logIn(tablet, username, aNewPassword);
    await column(tablet, 'Nuevos', customerName)
      .getByRole('button', { name: /^Preparar / })
      .click();
    await expect(column(tablet, 'En preparación', customerName)).toBeVisible();

    await toTheScanScreen(tablet);
    const found = tablet.getByRole('list', { name: 'Pedidos encontrados' }).getByRole('listitem');

    await tablet.getByRole('searchbox', { name: 'Buscar pedido' }).fill(customerName);
    await expect(found).toHaveCount(1);
    await tablet.getByRole('button', { name: `Listo ${code}` }).click();

    await expect(found).toContainText('Listo');
    await tablet.getByRole('button', { name: `Entregado ${code}` }).click();
    await expect(customer.getByRole('status')).toContainText(/entregado/i, { timeout: 15000 });

    await tablet.getByRole('button', { name: 'Deshacer' }).click();
    await expect(tablet.getByRole('button', { name: `Entregado ${code}` })).toBeVisible();

    await customer.context().close();
  });
});
