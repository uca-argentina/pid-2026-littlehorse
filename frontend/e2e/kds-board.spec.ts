import { expect, test } from '@playwright/test';
import {
  aFullName,
  aKdsAccount,
  aKdsAccountOffTonight,
  aNewPassword,
  anOrderPaidBy,
  kdsPath,
  logIn,
  loginPath,
} from './kds';
import { seededAdminPassword, seededAdminUsername } from './seeded-data';

/**
 * US-15, end to end against the real API, a real database and a real SignalR
 * connection: a paid order shows up on the bar's own board, with what a
 * bartender needs to make it, and it shows up live — nobody touches the
 * tablet for it to appear. Nobody but the bar's own account gets to see it.
 */

/** One card of the queue, as the API would send it, for screens tested on layout alone. */
function aMockedOrder(code: string, status: string, minutesAgo: number) {
  return {
    code,
    customerName: `Cliente ${code}`,
    status,
    paidAt: new Date(Date.now() - minutesAgo * 60_000).toISOString(),
    lastModifiedAt: status === 'Queued' ? null : new Date(Date.now() - 60_000).toISOString(),
    isForTable: false,
    orderItems: [{ productName: 'Aperol Spritz', quantity: 1, note: 'con mucho hielo' }],
  };
}

test.describe('KDS board', () => {
  test('shows a paid order under Nuevos, with who ordered and what they ordered', async ({
    page,
    browser,
    request,
  }) => {
    const username = await aKdsAccount(request);
    const customerName = aFullName();

    const customer = await browser.newPage();
    const drinkName = await anOrderPaidBy(customer, request, customerName);
    await customer.close();

    await logIn(page, username, aNewPassword);

    await expect(page).toHaveURL(new RegExp(`${kdsPath}$`));
    await expect(page.getByRole('banner')).toContainText(username);

    const card = page.getByRole('group', { name: 'Nuevos' }).getByRole('article').filter({
      hasText: customerName,
    });

    await expect(card).toContainText(drinkName);
  });

  // US-35, criterion 4: the station signs in, and its board says why it is
  // empty instead of showing a queue that is not its own to see.
  test('tells a station that is not in tonight’s night, without showing the queue', async ({
    page,
    request,
  }) => {
    const username = await aKdsAccountOffTonight(request);

    await logIn(page, username, aNewPassword);

    await expect(page).toHaveURL(new RegExp(`${kdsPath}$`));
    await expect(page.getByRole('alert')).toContainText(/no está en la noche de hoy/i);
    await expect(page.getByRole('group', { name: 'Nuevos' })).toHaveCount(0);
    await expect(page.getByText(/sin conexión/i)).toHaveCount(0);
  });

  // US-16, criteria 1 and 4, through the real API: the order moves column
  // and comes back, and the board redraws both times without a reload.
  test('takes an order into En preparación and hands it back to Nuevos', async ({
    page,
    browser,
    request,
  }) => {
    const username = await aKdsAccount(request);
    const customerName = aFullName();

    const customer = await browser.newPage();
    await anOrderPaidBy(customer, request, customerName);
    await customer.close();

    await logIn(page, username, aNewPassword);
    await expect(page).toHaveURL(new RegExp(`${kdsPath}$`));

    const column = (name: string) =>
      page.getByRole('group', { name }).getByRole('article').filter({ hasText: customerName });

    await column('Nuevos')
      .getByRole('button', { name: /^Preparar / })
      .click();
    await expect(column('En preparación')).toBeVisible();
    await expect(column('Nuevos')).toHaveCount(0);

    await column('En preparación')
      .getByRole('button', { name: /a la cola$/ })
      .click();
    await expect(column('Nuevos')).toBeVisible();
    await expect(column('En preparación')).toHaveCount(0);
  });

  // US-18 end to end: the order goes all the way to the counter, a mistaken
  // Entregado is taken back, and the customer's link follows along.
  test('takes an order through Listo and Entregado, and undoes the delivery', async ({
    page,
    browser,
    request,
  }) => {
    const username = await aKdsAccount(request);
    const customerName = aFullName();

    const customer = await browser.newPage();
    await anOrderPaidBy(customer, request, customerName);

    await logIn(page, username, aNewPassword);
    await expect(page).toHaveURL(new RegExp(`${kdsPath}$`));

    const column = (name: string) =>
      page.getByRole('group', { name }).getByRole('article').filter({ hasText: customerName });

    await column('Nuevos')
      .getByRole('button', { name: /^Preparar / })
      .click();
    await column('En preparación')
      .getByRole('button', { name: /^Listo / })
      .click();
    await expect(column('Listos en la barra')).toContainText('0 min');

    // The customer's own screen says so, without anybody telling them.
    await expect(customer.getByText(/está listo/i)).toBeVisible({ timeout: 15000 });

    await column('Listos en la barra')
      .getByRole('button', { name: /^Entregado / })
      .click();
    await expect(column('Listos en la barra')).toHaveCount(0);

    await page.getByRole('button', { name: 'Deshacer' }).click();
    await expect(column('Listos en la barra')).toBeVisible();
    await customer.close();
  });

  // Behind a bar, with wet hands, a tap lands wherever it lands: anywhere on a
  // card chooses it, not only its top row. Where a tap lands is layout, so it
  // is proved here and not in a unit test.
  test('chooses an order by tapping anywhere on its card', async ({ page, request }) => {
    const username = await aKdsAccount(request);

    await page.route('**/api/kds/queue', (route) =>
      route.fulfill({ json: [aMockedOrder('Z-2000', 'Queued', 5)] }),
    );

    await logIn(page, username, aNewPassword);
    await expect(page).toHaveURL(new RegExp(`${kdsPath}$`));

    // A tap at the spot, not a click on the text node: what answers is whatever
    // the browser finds under the finger, which is exactly what is being proved.
    const drinks = await page.getByText('con mucho hielo').boundingBox();
    await page.mouse.click(drinks!.x + drinks!.width / 2, drinks!.y + drinks!.height / 2);

    await expect(page.getByRole('button', { name: 'Elegir Z-2000' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
  });

  // The row that holds Preparar spans the card: its empty part, left of the
  // button and down to the border, is card too and must choose it.
  test('chooses an order from the empty space beside Preparar', async ({ page, request }) => {
    const username = await aKdsAccount(request);

    await page.route('**/api/kds/queue', (route) =>
      route.fulfill({ json: [aMockedOrder('Z-2100', 'Queued', 5)] }),
    );

    await logIn(page, username, aNewPassword);
    await expect(page).toHaveURL(new RegExp(`${kdsPath}$`));

    const card = page.getByRole('group', { name: 'Nuevos' }).getByRole('article');
    const choose = page.getByRole('button', { name: 'Elegir Z-2100' });
    const cardBox = (await card.boundingBox())!;
    const prepare = (await page.getByRole('button', { name: 'Preparar Z-2100' }).boundingBox())!;

    // Level with Preparar, well to its left.
    await page.mouse.click(cardBox.x + 30, prepare.y + prepare.height / 2);
    await expect(choose).toHaveAttribute('aria-pressed', 'true');

    // Near the bottom border. Measured again: once chosen, "Elegido" stands
    // where Preparar was and the card is shorter.
    const chosenBox = (await card.boundingBox())!;
    await page.mouse.click(chosenBox.x + chosenBox.width / 3, chosenBox.y + chosenBox.height - 5);
    await expect(choose).toHaveAttribute('aria-pressed', 'false');
  });

  // Nuevos grows all night; En preparación and Listos stay short. Scrolling one
  // column must not drag the others, nor the header with the venue on it.
  test('scrolls each column on its own, under a header that stays', async ({ page, request }) => {
    const username = await aKdsAccount(request);
    const queue = [
      ...Array.from({ length: 20 }, (_, index) =>
        aMockedOrder(`Z-${String(3000 + index)}`, 'Queued', 40 - index),
      ),
      aMockedOrder('Z-4000', 'InPreparation', 3),
      aMockedOrder('Z-5000', 'Ready', 2),
    ];

    await page.setViewportSize({ width: 1280, height: 800 });
    await page.route('**/api/kds/queue', (route) => route.fulfill({ json: queue }));

    await logIn(page, username, aNewPassword);
    await expect(page).toHaveURL(new RegExp(`${kdsPath}$`));

    const inPreparation = page.getByRole('group', { name: 'En preparación' }).getByRole('article');
    const before = await inPreparation.boundingBox();

    await page.getByRole('group', { name: 'Nuevos' }).getByRole('article').first().hover();
    await page.mouse.wheel(0, 5000);

    await expect(
      page
        .getByRole('group', { name: 'Nuevos' })
        .getByRole('article')
        .filter({ hasText: 'Z-3019' }),
    ).toBeInViewport();
    await expect(page.getByRole('banner')).toBeInViewport();
    expect(await inPreparation.boundingBox()).toEqual(before);
  });

  // Deep in a long Nuevos, the oldest orders — the ones to make first — are
  // out of sight. One tap brings the column back to them, and only that column.
  test('brings a scrolled column back to the top with one tap', async ({ page, request }) => {
    const username = await aKdsAccount(request);
    const queue = [
      ...Array.from({ length: 20 }, (_, index) =>
        aMockedOrder(`Z-${String(6000 + index)}`, 'Queued', 40 - index),
      ),
      aMockedOrder('Z-7000', 'InPreparation', 3),
    ];

    await page.setViewportSize({ width: 1280, height: 800 });
    await page.route('**/api/kds/queue', (route) => route.fulfill({ json: queue }));

    await logIn(page, username, aNewPassword);
    await expect(page).toHaveURL(new RegExp(`${kdsPath}$`));

    const nuevos = page.getByRole('group', { name: 'Nuevos' });
    const backToTop = nuevos.getByRole('button', { name: 'Volver arriba' });

    await expect(backToTop).toHaveCount(0);

    await nuevos.getByRole('article').first().hover();
    await page.mouse.wheel(0, 5000);
    await expect(backToTop).toBeInViewport();
    await expect(
      page
        .getByRole('group', { name: 'En preparación' })
        .getByRole('button', { name: 'Volver arriba' }),
    ).toHaveCount(0);

    await backToTop.click();

    await expect(nuevos.getByRole('article').filter({ hasText: 'Z-6000' })).toBeInViewport();
    await expect(backToTop).toHaveCount(0);
  });

  // Like Safari's overlay scrollbars, in every browser: no native bar taking
  // width from the cards, and a thin thumb that shows while the column moves
  // and fades out once it stops.
  test('shows where a column is only while it scrolls', async ({ page, request }) => {
    const username = await aKdsAccount(request);
    const queue = Array.from({ length: 20 }, (_, index) =>
      aMockedOrder(`Z-${String(8000 + index)}`, 'Queued', 40 - index),
    );

    await page.setViewportSize({ width: 1280, height: 800 });
    await page.route('**/api/kds/queue', (route) => route.fulfill({ json: queue }));

    await logIn(page, username, aNewPassword);
    await expect(page).toHaveURL(new RegExp(`${kdsPath}$`));

    const nuevos = page.getByRole('group', { name: 'Nuevos' });
    const thumb = nuevos.getByTestId('scroll-thumb');

    await expect(nuevos.locator('.stack')).toHaveCSS('scrollbar-width', 'none');
    await expect(thumb).toHaveCSS('opacity', '0');

    await nuevos.getByRole('article').first().hover();
    await page.mouse.wheel(0, 1500);
    await expect(thumb).not.toHaveCSS('opacity', '0');

    await expect(thumb).toHaveCSS('opacity', '0', { timeout: 3000 });
  });

  // A failure is only worth saying if it is seen. With a long queue the page
  // is taller than the tablet, and a warning drawn at the end of it is below
  // the fold: the bartender pressed Preparar at the top and saw nothing.
  // Layout is what this proves, which is why it is here and not in a unit test.
  test('shows a failed Preparar where it can be seen, however long the queue', async ({
    page,
    request,
  }) => {
    const username = await aKdsAccount(request);
    const longQueue = Array.from({ length: 20 }, (_, index) =>
      aMockedOrder(`Z-${String(1000 + index)}`, 'Queued', 30 - index),
    );

    await page.setViewportSize({ width: 1280, height: 800 });
    await page.route('**/api/kds/queue', (route) => route.fulfill({ json: longQueue }));
    await page.route('**/api/kds/orders/*/start-preparing', (route) =>
      route.fulfill({ status: 500, json: { type: 'about:blank' } }),
    );

    await logIn(page, username, aNewPassword);
    await expect(page).toHaveURL(new RegExp(`${kdsPath}$`));

    await page.getByRole('button', { name: 'Preparar Z-1000' }).click();

    await expect(page.getByRole('alert')).toContainText('No pudimos tomar el pedido Z-1000');
    await expect(page.getByRole('alert')).toBeInViewport();
  });

  // The one thing a unit test cannot prove: the hub, the token in the
  // connection, and the proxy all actually agree with each other.
  test('shows a new order live, without anybody reloading the tablet', async ({
    page,
    browser,
    request,
  }) => {
    const username = await aKdsAccount(request);
    const customerName = aFullName();

    await logIn(page, username, aNewPassword);
    await expect(page).toHaveURL(new RegExp(`${kdsPath}$`));

    const customer = await browser.newPage();
    await anOrderPaidBy(customer, request, customerName);
    await customer.close();

    await expect(page.getByText(customerName)).toBeVisible({ timeout: 15000 });
  });

  // Criterion 3: not even by typing the address, once signed in as anyone else.
  test('turns away an administrator', async ({ page }) => {
    await page.goto(loginPath);
    await page.getByRole('textbox', { name: /usuario/i }).fill(seededAdminUsername);
    await page.getByRole('textbox', { name: /contraseña/i }).fill(seededAdminPassword());
    await page.getByRole('button', { name: /entrar/i }).click();

    await page.goto(kdsPath);

    await expect(page).not.toHaveURL(new RegExp(`${kdsPath}$`));
  });

  // Criterion 4 is covered in KdsBoardPage's own Vitest spec, with a
  // controlled empty response: the shared development database this suite
  // runs against is never reset between runs, so nothing here can promise the
  // queue is actually empty — only a mock can.
});
