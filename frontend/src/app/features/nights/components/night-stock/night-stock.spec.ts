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
  lines: [
    {
      productId: 'fernet',
      productName: 'Fernet',
      carried: 8,
      added: 0,
      loaded: 8,
      sold: 0,
      left: 8,
    },
    {
      productId: 'gin',
      productName: 'Gin Tonic',
      carried: 20,
      added: 12,
      loaded: 32,
      sold: 7,
      left: 25,
    },
  ],
};

function withGin(line: Partial<NightStockSheet['lines'][number]>): NightStockSheet {
  return { ...sheet, lines: [sheet.lines[0], { ...sheet.lines[1], ...line }] };
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

function rowOf(name: string): HTMLElement {
  return screen.getByRole('listitem', { name });
}

function type(row: HTMLElement, units: string): void {
  fireEvent.input(within(row).getByLabelText(/unidades/i), { target: { value: units } });
}

function press(row: HTMLElement, name: RegExp): void {
  within(row).getByRole('button', { name }).click();
}

describe('NightStock', () => {
  it('shows what each product starts with, what was loaded, sold and what is left', async () => {
    await openStockShowing(sheet, 'underway');

    const gin = rowOf('Gin Tonic');
    expect(within(gin).getByText(/arrancó con 20/i)).not.toBeNull();
    expect(within(gin).getByText(/cargadas \+12/i)).not.toBeNull();
    expect(within(gin).getByText(/vendidas 7/i)).not.toBeNull();
    expect(within(gin).getByText(/quedan 25/i)).not.toBeNull();
  });

  it('says what an upcoming night will start with', async () => {
    await openStockShowing(sheet, 'upcoming');

    expect(within(rowOf('Fernet')).getByText(/arranca con 8/i)).not.toBeNull();
  });

  it('loads units into the night and shows the stock the API answers', async () => {
    const { rendered, adjustStock } = await openStockShowing(
      sheet,
      'upcoming',
      vi.fn().mockReturnValue(of(withGin({ added: 22, loaded: 42, left: 35 }))),
    );

    type(rowOf('Gin Tonic'), '10');
    press(rowOf('Gin Tonic'), /sumar/i);
    await rendered.fixture.whenStable();

    expect(adjustStock).toHaveBeenCalledWith('night-1', 'gin', 10);
    expect(within(rowOf('Gin Tonic')).getByText(/quedan 35/i)).not.toBeNull();
    expect((within(rowOf('Gin Tonic')).getByLabelText(/unidades/i) as HTMLInputElement).value).toBe(
      '',
    );
  });

  it('takes units out when they were loaded wrong', async () => {
    const { rendered, adjustStock } = await openStockShowing(sheet, 'underway');

    type(rowOf('Gin Tonic'), '5');
    press(rowOf('Gin Tonic'), /restar/i);
    await rendered.fixture.whenStable();

    expect(adjustStock).toHaveBeenCalledWith('night-1', 'gin', -5);
  });

  it('does not send units that are not a whole number above zero', async () => {
    const { rendered, adjustStock } = await openStockShowing(sheet, 'underway');

    type(rowOf('Gin Tonic'), '0');
    press(rowOf('Gin Tonic'), /sumar/i);
    await rendered.fixture.whenStable();

    expect(adjustStock).not.toHaveBeenCalled();
    expect(within(rowOf('Gin Tonic')).getByRole('alert').textContent).toMatch(/número entero/i);
  });

  it('says so on the row when sales left less than it takes away, and reloads the stock', async () => {
    const { rendered, http } = await openStockShowing(
      sheet,
      'underway',
      vi.fn().mockReturnValue(throwError(() => refusal(ProblemTypes.productStockMoved))),
    );

    type(rowOf('Gin Tonic'), '30');
    press(rowOf('Gin Tonic'), /restar/i);
    TestBed.tick();
    http.expectOne(STOCK_URL).flush(withGin({ sold: 15, left: 17 }));
    await rendered.fixture.whenStable();

    expect(within(rowOf('Gin Tonic')).getByRole('alert').textContent).toMatch(/se vendió/i);
    expect(within(rowOf('Gin Tonic')).getByText(/quedan 17/i)).not.toBeNull();
  });

  it('says to wait when an earlier night has not ended', async () => {
    const { rendered } = await openStockShowing(
      sheet,
      'upcoming',
      vi.fn().mockReturnValue(throwError(() => refusal(ProblemTypes.nightEarlierNightOpen))),
    );

    type(rowOf('Gin Tonic'), '10');
    press(rowOf('Gin Tonic'), /sumar/i);
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toMatch(/noche anterior/i);
  });

  it('shows a finished night read-only, with what it loaded, sold and left', async () => {
    await openStockShowing(sheet, 'over');

    const gin = rowOf('Gin Tonic');
    expect(within(gin).getByText(/se cargaron 32/i)).not.toBeNull();
    expect(within(gin).getByText(/se vendieron 7/i)).not.toBeNull();
    expect(within(gin).getByText(/quedaron 25/i)).not.toBeNull();
    expect(screen.queryByRole('button', { name: /sumar/i })).toBeNull();
  });

  it('says the menu is empty instead of showing nothing', async () => {
    await openStockShowing({ nightId: 'night-1', lines: [] }, 'upcoming');

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

    expect(rowOf('Gin Tonic')).not.toBeNull();
  });
});
