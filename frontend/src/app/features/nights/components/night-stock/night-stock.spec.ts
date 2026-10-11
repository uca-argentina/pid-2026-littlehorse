import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { fireEvent, render, screen, within } from '@testing-library/angular';
import { of, throwError } from 'rxjs';
import { ProblemTypes } from '../../../../core/api/problem-types';
import { NightsService, nightStockUrl } from '../../nights.service';
import type { NightStockLine } from '../../nights.service';
import { NightStock } from './night-stock';

const url = nightStockUrl('night-1');

const lines: NightStockLine[] = [
  { productId: 'gin', productName: 'Gin Tonic', loaded: 20, sold: 3, remaining: 17 },
  { productId: 'fernet', productName: 'Fernet con Coca', loaded: 8, sold: 8, remaining: 0 },
];

async function openScreen(editable = true, adjustStock = vi.fn()) {
  const rendered = await render(NightStock, {
    inputs: { nightId: 'night-1', editable },
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: NightsService, useValue: { adjustStock } },
    ],
  });

  return { rendered, adjustStock, http: TestBed.inject(HttpTestingController) };
}

async function openScreenShowing(stock = lines, editable = true, adjustStock = vi.fn()) {
  const opened = await openScreen(editable, adjustStock);

  opened.http.expectOne(url).flush(stock);
  await opened.rendered.fixture.whenStable();

  return opened;
}

function rowOf(name: RegExp): HTMLElement {
  return screen.getByRole('listitem', { name });
}

function typeUnits(name: RegExp, units: string): void {
  fireEvent.input(within(rowOf(name)).getByRole('spinbutton'), { target: { value: units } });
}

function press(name: RegExp, inRow: RegExp): void {
  within(rowOf(inRow)).getByRole('button', { name }).click();
}

describe('NightStock', () => {
  // Criterion 3: loaded, sold and left, product by product.
  it('shows what each product loaded, sold and has left', async () => {
    await openScreenShowing();

    const gin = rowOf(/gin tonic/i);

    expect(within(gin).getByText('20')).not.toBeNull();
    expect(within(gin).getByText('3')).not.toBeNull();
    expect(within(gin).getByText('17')).not.toBeNull();
    expect(within(rowOf(/fernet/i)).getByText(/se acabó/i)).not.toBeNull();
  });

  it('says the stock is waiting while the night before is not over', async () => {
    const { rendered, http } = await openScreen();

    http
      .expectOne(url)
      .flush(
        { type: ProblemTypes.nightStockPreviousNightNotOver, detail: 'x' },
        { status: 409, statusText: 'Conflict' },
      );
    await rendered.fixture.whenStable();

    expect(screen.getByRole('status').textContent).toMatch(/cuando termine la noche anterior/i);
    expect(screen.queryByRole('list')).toBeNull();
  });

  it('says so when the stock could not be loaded, and tries again on request', async () => {
    const { rendered, http } = await openScreen();

    http.expectOne(url).error(new ProgressEvent('error'));
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toMatch(/no pudimos traer el stock/i);

    screen.getByRole('button', { name: /reintentar/i }).click();
    rendered.fixture.detectChanges();
    http.expectOne(url).flush(lines);
    await rendered.fixture.whenStable();

    expect(screen.getByRole('listitem', { name: /gin tonic/i })).not.toBeNull();
  });

  it('says there is nothing to count when the venue has no products', async () => {
    await openScreenShowing([]);

    expect(screen.getByText(/todavía no hay productos/i)).not.toBeNull();
  });

  // Units are added, not set: what is sent is the change, and the figures
  // that come back are what the screen draws.
  it('adds the units that arrived', async () => {
    const adjustStock = vi
      .fn()
      .mockReturnValue(of({ productId: 'gin', loaded: 32, sold: 3, remaining: 29 }));
    const { rendered, http } = await openScreenShowing(lines, true, adjustStock);

    typeUnits(/gin tonic/i, '12');
    press(/sumar/i, /gin tonic/i);
    rendered.fixture.detectChanges();

    expect(adjustStock).toHaveBeenCalledWith('night-1', 'gin', 12);

    http.expectOne(url).flush([{ ...lines[0], loaded: 32, remaining: 29 }, lines[1]]);
    await rendered.fixture.whenStable();

    expect(within(rowOf(/gin tonic/i)).getByText('29')).not.toBeNull();
  });

  it('takes away what was loaded by mistake, sending a negative change', async () => {
    const adjustStock = vi
      .fn()
      .mockReturnValue(of({ productId: 'gin', loaded: 15, sold: 3, remaining: 12 }));
    const { rendered, http } = await openScreenShowing(lines, true, adjustStock);

    typeUnits(/gin tonic/i, '5');
    press(/restar/i, /gin tonic/i);
    rendered.fixture.detectChanges();

    expect(adjustStock).toHaveBeenCalledWith('night-1', 'gin', -5);
    http.expectOne(url).flush(lines);
    await rendered.fixture.whenStable();
  });

  it.each([
    ['nothing', ''],
    ['zero', '0'],
    ['a negative number', '-3'],
    ['a number that is not whole', '1.5'],
  ])('refuses to move %s units, and says so', async (_case, units) => {
    const { rendered, adjustStock } = await openScreenShowing();

    typeUnits(/gin tonic/i, units);
    press(/sumar/i, /gin tonic/i);
    await rendered.fixture.whenStable();

    expect(adjustStock).not.toHaveBeenCalled();
    expect(within(rowOf(/gin tonic/i)).getByRole('alert').textContent).toMatch(
      /número entero mayor a cero/i,
    );
  });

  // The screen showed 17 and the bar sold 15 while it was open.
  it('says sales made meanwhile left less than the change takes away, and shows what is left', async () => {
    const adjustStock = vi.fn().mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { type: ProblemTypes.nightStockMoved },
          }),
      ),
    );
    const { rendered, http } = await openScreenShowing(lines, true, adjustStock);

    typeUnits(/gin tonic/i, '10');
    press(/restar/i, /gin tonic/i);
    rendered.fixture.detectChanges();

    http.expectOne(url).flush([{ ...lines[0], sold: 18, remaining: 2 }, lines[1]]);
    await rendered.fixture.whenStable();

    expect(within(rowOf(/gin tonic/i)).getByRole('alert').textContent).toMatch(
      /se vendió mientras tanto/i,
    );
    expect(within(rowOf(/gin tonic/i)).getByText('2')).not.toBeNull();
  });

  it('says so when the change could not be saved', async () => {
    const adjustStock = vi
      .fn()
      .mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    const { rendered } = await openScreenShowing(lines, true, adjustStock);

    typeUnits(/gin tonic/i, '3');
    press(/sumar/i, /gin tonic/i);
    await rendered.fixture.whenStable();

    expect(within(rowOf(/gin tonic/i)).getByRole('alert').textContent).toMatch(
      /no pudimos guardarlo/i,
    );
  });

  // Criterion 3 is a night that ended: it is read, never moved.
  it('offers no way to move the stock of a night that ended', async () => {
    await openScreenShowing(lines, false);

    expect(screen.queryByRole('spinbutton')).toBeNull();
    expect(screen.queryByRole('button', { name: /sumar|restar/i })).toBeNull();
    expect(within(rowOf(/gin tonic/i)).getByText('17')).not.toBeNull();
  });
});
