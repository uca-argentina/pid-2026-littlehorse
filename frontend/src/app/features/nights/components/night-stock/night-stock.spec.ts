import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { fireEvent, render, screen, within } from '@testing-library/angular';
import { of, throwError } from 'rxjs';
import { ProblemTypes } from '../../../../core/api/problem-types';
import type { NightPhase } from '../../night-calendar';
import { NIGHTS_URL, NightsService } from '../../nights.service';
import type { NightStock as NightStockSheet } from '../../nights.service';
import { NightStock } from './night-stock';

const STOCK_URL = `${NIGHTS_URL}/night-1/stock`;

const sheet: NightStockSheet = {
  nightId: 'night-1',
  bars: [
    {
      kdsId: 'main',
      name: 'main-bar',
      lines: [
        {
          productId: 'fernet',
          productName: 'Fernet',
          loaded: 0,
          sold: 0,
          left: 0,
          inStoreroom: 10,
        },
        {
          productId: 'gin',
          productName: 'Gin Tonic',
          loaded: 12,
          sold: 7,
          left: 5,
          inStoreroom: 18,
        },
      ],
    },
    {
      kdsId: 'vip',
      name: 'vip-bar',
      lines: [
        {
          productId: 'fernet',
          productName: 'Fernet',
          loaded: 0,
          sold: 0,
          left: 0,
          inStoreroom: 10,
        },
        {
          productId: 'gin',
          productName: 'Gin Tonic',
          loaded: 6,
          sold: 1,
          left: 5,
          inStoreroom: 18,
        },
      ],
    },
  ],
};

/** The sheet with the main bar's gin changed, and the storeroom with it when given. */
function withMainGin(
  line: Partial<NightStockSheet['bars'][number]['lines'][number]>,
): NightStockSheet {
  const [main, vip] = sheet.bars;

  return {
    ...sheet,
    bars: [{ ...main, lines: [main.lines[0], { ...main.lines[1], ...line }] }, vip],
  };
}

function refusal(type: string): HttpErrorResponse {
  return new HttpErrorResponse({ status: 409, error: { type } });
}

async function openStock(phase: NightPhase, adjustStock = vi.fn().mockReturnValue(of(sheet))) {
  const rendered = await render(NightStock, {
    inputs: { nightId: 'night-1', phase },
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: NightsService, useValue: { adjustStock } },
    ],
  });
  const http = TestBed.inject(HttpTestingController);

  return { rendered, http, adjustStock };
}

async function openStockShowing(
  shown: NightStockSheet,
  phase: NightPhase,
  adjustStock?: Parameters<typeof openStock>[1],
) {
  const opened = await openStock(phase, adjustStock);

  opened.http.expectOne(STOCK_URL).flush(shown);
  await opened.rendered.fixture.whenStable();

  return opened;
}

function rowOf(bar: string, product: string): HTMLElement {
  return within(screen.getByRole('region', { name: bar })).getByRole('listitem', { name: product });
}

function type(row: HTMLElement, units: string): void {
  fireEvent.input(within(row).getByLabelText(/unidades/i), { target: { value: units } });
}

function press(row: HTMLElement, name: RegExp): void {
  within(row).getByRole('button', { name }).click();
}

describe('NightStock', () => {
  it('shows each bar with what it loaded, sold and has left, and what is in the storeroom', async () => {
    await openStockShowing(sheet, 'underway');

    const gin = rowOf('main-bar', 'Gin Tonic');
    expect(within(gin).getByText(/cargadas 12/i)).not.toBeNull();
    expect(within(gin).getByText(/vendidas 7/i)).not.toBeNull();
    expect(within(gin).getByText(/quedan 5/i)).not.toBeNull();
    expect(within(gin).getByText(/en el depósito 18/i)).not.toBeNull();
    expect(within(rowOf('vip-bar', 'Gin Tonic')).getByText(/cargadas 6/i)).not.toBeNull();
  });

  it('loads units from the storeroom into one bar and shows the stock the API answers', async () => {
    const { rendered, adjustStock } = await openStockShowing(
      sheet,
      'upcoming',
      vi.fn().mockReturnValue(of(withMainGin({ loaded: 22, left: 15, inStoreroom: 8 }))),
    );

    type(rowOf('main-bar', 'Gin Tonic'), '10');
    press(rowOf('main-bar', 'Gin Tonic'), /cargar/i);
    await rendered.fixture.whenStable();

    expect(adjustStock).toHaveBeenCalledWith('night-1', 'main', 'gin', 10);
    expect(within(rowOf('main-bar', 'Gin Tonic')).getByText(/quedan 15/i)).not.toBeNull();
    expect(
      (within(rowOf('main-bar', 'Gin Tonic')).getByLabelText(/unidades/i) as HTMLInputElement)
        .value,
    ).toBe('');
  });

  it('gives units back to the storeroom', async () => {
    const { rendered, adjustStock } = await openStockShowing(sheet, 'underway');

    type(rowOf('vip-bar', 'Gin Tonic'), '5');
    press(rowOf('vip-bar', 'Gin Tonic'), /devolver/i);
    await rendered.fixture.whenStable();

    expect(adjustStock).toHaveBeenCalledWith('night-1', 'vip', 'gin', -5);
  });

  it('does not send units that are not a whole number above zero', async () => {
    const { rendered, adjustStock } = await openStockShowing(sheet, 'underway');

    type(rowOf('main-bar', 'Gin Tonic'), '0');
    press(rowOf('main-bar', 'Gin Tonic'), /cargar/i);
    await rendered.fixture.whenStable();

    expect(adjustStock).not.toHaveBeenCalled();
    expect(within(rowOf('main-bar', 'Gin Tonic')).getByRole('alert').textContent).toMatch(
      /número entero/i,
    );
  });

  it('says so on the row when the storeroom has less than it loads, and reloads the stock', async () => {
    const { rendered, http } = await openStockShowing(
      sheet,
      'underway',
      vi.fn().mockReturnValue(throwError(() => refusal(ProblemTypes.nightStoreroomShort))),
    );

    type(rowOf('main-bar', 'Gin Tonic'), '30');
    press(rowOf('main-bar', 'Gin Tonic'), /cargar/i);
    TestBed.tick();
    http.expectOne(STOCK_URL).flush(withMainGin({ inStoreroom: 4 }));
    await rendered.fixture.whenStable();

    const gin = rowOf('main-bar', 'Gin Tonic');
    expect(within(gin).getByRole('alert').textContent).toMatch(/depósito/i);
    expect(within(gin).getByText(/en el depósito 4/i)).not.toBeNull();
  });

  it('says so on the row when the bar sold meanwhile and has less than it gives back', async () => {
    const { rendered, http } = await openStockShowing(
      sheet,
      'underway',
      vi.fn().mockReturnValue(throwError(() => refusal(ProblemTypes.productStockMoved))),
    );

    type(rowOf('main-bar', 'Gin Tonic'), '5');
    press(rowOf('main-bar', 'Gin Tonic'), /devolver/i);
    TestBed.tick();
    http.expectOne(STOCK_URL).flush(withMainGin({ sold: 9, left: 3 }));
    await rendered.fixture.whenStable();

    const gin = rowOf('main-bar', 'Gin Tonic');
    expect(within(gin).getByRole('alert').textContent).toMatch(/vendió/i);
    expect(within(gin).getByText(/quedan 3/i)).not.toBeNull();
  });

  it('says so when the night ended while loading', async () => {
    const { rendered } = await openStockShowing(
      sheet,
      'underway',
      vi.fn().mockReturnValue(throwError(() => refusal(ProblemTypes.nightStockClosed))),
    );

    type(rowOf('main-bar', 'Gin Tonic'), '10');
    press(rowOf('main-bar', 'Gin Tonic'), /cargar/i);
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toMatch(/la noche terminó/i);
  });

  it('says so when the bar was taken out of the night while loading', async () => {
    const { rendered } = await openStockShowing(
      sheet,
      'upcoming',
      vi.fn().mockReturnValue(throwError(() => refusal(ProblemTypes.nightBarNotInNight))),
    );

    type(rowOf('vip-bar', 'Gin Tonic'), '10');
    press(rowOf('vip-bar', 'Gin Tonic'), /cargar/i);
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toMatch(/ya no trabaja/i);
  });

  it('shows a finished night read-only, with what each bar loaded, sold and left', async () => {
    await openStockShowing(sheet, 'over');

    const gin = rowOf('main-bar', 'Gin Tonic');
    expect(within(gin).getByText(/se cargaron 12/i)).not.toBeNull();
    expect(within(gin).getByText(/se vendieron 7/i)).not.toBeNull();
    expect(within(gin).getByText(/quedaron 5/i)).not.toBeNull();
    expect(screen.queryByRole('button', { name: /cargar/i })).toBeNull();
  });

  it('says the menu is empty instead of showing nothing', async () => {
    await openStockShowing(
      { nightId: 'night-1', bars: [{ kdsId: 'main', name: 'main-bar', lines: [] }] },
      'upcoming',
    );

    expect(screen.getByText(/no hay productos en la carta/i)).not.toBeNull();
  });

  it('offers to retry when the stock could not be loaded', async () => {
    const { rendered, http } = await openStock('underway');

    http.expectOne(STOCK_URL).flush('', { status: 500, statusText: 'Server Error' });
    await rendered.fixture.whenStable();

    screen.getByRole('button', { name: /reintentar/i }).click();
    TestBed.tick();
    http.expectOne(STOCK_URL).flush(sheet);
    await rendered.fixture.whenStable();

    expect(rowOf('main-bar', 'Gin Tonic')).not.toBeNull();
  });
});
