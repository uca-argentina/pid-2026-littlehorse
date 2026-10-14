import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { ProblemTypes } from '../../../core/api/problem-types';
import { NIGHTS_URL } from '../../nights/nights.service';
import type { Night } from '../../nights/nights.service';
import type { NightMetrics } from '../reports.service';
import { REPORT_REFRESH_MS, ReportsPage } from './reports.page';

const noAudit = { createdAt: null, createdBy: null, lastModifiedAt: null, lastModifiedBy: null };

function aNight(id: string, name: string, day: number): Night {
  return {
    id,
    name,
    startsAt: new Date(2026, 9, day, 23, 0).toISOString(),
    endsAt: new Date(2026, 9, day + 1, 6, 0).toISOString(),
    crewIds: [],
    audit: noAudit,
  };
}

const friday = aNight('friday', 'Viernes 9', 9);
const saturday = aNight('saturday', 'Sábado 10', 10);
const nextFriday = aNight('next-friday', 'Viernes 16', 16);

const duringSaturday = new Date(2026, 9, 11, 1, 0);
const onSunday = new Date(2026, 9, 11, 15, 0);

const saturdaysReport: NightMetrics = {
  nightId: 'saturday',
  sold: 1284500,
  orders: 312,
  averageTicket: 4117,
  units: 604,
  byMethod: [
    { method: 'Cash', orders: 148, amount: 590900 },
    { method: 'Digital', orders: 121, amount: 488100 },
    { method: 'VipBalance', orders: 43, amount: 205500 },
  ],
  paidToReady: { averageSeconds: 420, worstTenthSeconds: 1080 },
  readyToDelivered: { averageSeconds: 180, worstTenthSeconds: 660 },
  byBar: [
    {
      kdsId: 'pista',
      name: 'Barra Pista',
      paidToReady: { averageSeconds: 360, worstTenthSeconds: 900 },
    },
    {
      kdsId: 'vip',
      name: 'Barra VIP',
      paidToReady: { averageSeconds: null, worstTenthSeconds: null },
    },
  ],
  unmeasured: 0,
  top: [
    {
      productId: 'fernet',
      name: 'Fernet con Coca',
      units: 142,
      billed: 568000,
      loaded: 160,
      soldOut: false,
    },
    { productId: 'gin', name: 'Gin Tonic', units: 96, billed: 432000, loaded: 96, soldOut: true },
  ],
  soldOutProducts: 1,
};

const metricsOf = (id: string) => `${NIGHTS_URL}/${id}/metrics`;

async function openScreen(now: Date, nightId?: string) {
  vi.setSystemTime(now);

  const rendered = await render(ReportsPage, {
    inputs: { venueSlug: 'bar-alfa', ...(nightId === undefined ? {} : { nightId }) },
    providers: [
      provideRouter([
        { path: ':venueSlug/staff/reports/:nightId', children: [] },
        { path: ':venueSlug/staff/nights', children: [] },
        { path: ':venueSlug/staff/nights/:id', children: [] },
      ]),
      provideHttpClient(),
      provideHttpClientTesting(),
    ],
  });

  return { rendered, http: TestBed.inject(HttpTestingController), router: TestBed.inject(Router) };
}

/** A night's report on screen: the nights for the picker, then its numbers. */
async function openReport(now: Date, report: NightMetrics = saturdaysReport) {
  const opened = await openScreen(now, report.nightId);

  opened.http.expectOne(NIGHTS_URL).flush([nextFriday, saturday, friday]);
  opened.http.expectOne(metricsOf(report.nightId)).flush(report);
  await opened.rendered.fixture.whenStable();

  return opened;
}

describe('ReportsPage', () => {
  beforeEach(() => vi.useFakeTimers({ toFake: ['Date'] }));
  afterEach(() => vi.useRealTimers());

  describe('without a night chosen', () => {
    it('opens on the night underway', async () => {
      const { rendered, http, router } = await openScreen(duringSaturday);

      http.expectOne(NIGHTS_URL).flush([nextFriday, saturday, friday]);
      await rendered.fixture.whenStable();

      expect(router.url).toBe('/bar-alfa/staff/reports/saturday');
    });

    it('opens on the last night that ended when none is on', async () => {
      const { rendered, http, router } = await openScreen(onSunday);

      http.expectOne(NIGHTS_URL).flush([friday, saturday]);
      await rendered.fixture.whenStable();

      expect(router.url).toBe('/bar-alfa/staff/reports/saturday');
    });

    it('says there is nothing to show before any night has started', async () => {
      const { rendered, http } = await openScreen(onSunday);

      http.expectOne(NIGHTS_URL).flush([nextFriday]);
      await rendered.fixture.whenStable();

      expect(screen.getByText(/todavía no hay noches para mostrar/i)).not.toBeNull();
      expect(screen.getByRole('link', { name: /ir a noches/i }).getAttribute('href')).toBe(
        '/bar-alfa/staff/nights',
      );
    });
  });

  // US-48.
  it('shows what the night sold, how many orders and by which method', async () => {
    await openReport(onSunday);

    const sold = screen.getByTestId('sold');
    expect(sold.textContent).toContain('1.284.500');
    expect(screen.getByTestId('orders').textContent).toContain('312');

    const cash = screen.getByTestId('method-Cash');
    expect(cash.textContent).toContain('Efectivo');
    expect(cash.textContent).toContain('148 pedidos');
    expect(cash.textContent).toContain('590.900');
    expect(screen.getByTestId('method-VipBalance').textContent).toContain('Saldo VIP');
  });

  // US-47, criteria 1 and 2.
  it('shows how long orders took, overall and bar by bar', async () => {
    await openReport(onSunday);

    const ready = screen.getByTestId('paid-to-ready');
    expect(ready.textContent).toContain('7 min');
    expect(ready.textContent).toContain('18 min');
    expect(screen.getByTestId('bar-pista').textContent).toContain('6 min');
    // A bar that made nothing yet has no time, which is not "0 min".
    expect(screen.getByTestId('bar-vip').textContent).toContain('—');
  });

  // US-47, criterion 3.
  it('says how many orders could not be measured', async () => {
    await openReport(onSunday, { ...saturdaysReport, unmeasured: 12 });

    expect(screen.getByText(/12 pedidos no se pudieron medir/i)).not.toBeNull();
  });

  it('says nothing about unmeasured orders when every one was measured', async () => {
    await openReport(onSunday);

    expect(screen.queryByText(/no se pudieron medir/i)).toBeNull();
  });

  // US-49, criteria 1 and 2.
  it('ranks the most ordered products against what was loaded', async () => {
    await openReport(onSunday);

    const gin = screen.getByTestId('top-gin');
    expect(gin.textContent).toContain('Gin Tonic');
    expect(gin.textContent).toContain('96');
    expect(gin.textContent).toContain('432.000');
    expect(gin.textContent).toContain('de 96 cargadas');
    expect(gin.textContent).toContain('Se agotó');
    expect(screen.getByTestId('top-fernet').textContent).toContain('Sobró');
  });

  it('names the night and links back to it', async () => {
    await openReport(onSunday);

    expect((screen.getByLabelText(/noche/i) as HTMLSelectElement).value).toBe('saturday');
    expect(screen.getByText('Terminada')).not.toBeNull();
    expect(screen.getByRole('link', { name: /ver la noche/i }).getAttribute('href')).toBe(
      '/bar-alfa/staff/nights/saturday',
    );
  });

  // Only the nights that have something to report: the next Friday has not started.
  it('offers the nights that have started in the picker, the latest first', async () => {
    await openReport(onSunday);

    const options = [...(screen.getByLabelText(/noche/i) as HTMLSelectElement).options];
    expect(options.map((option) => option.value)).toEqual(['saturday', 'friday']);
  });

  it('switches to the night chosen in the picker', async () => {
    const { rendered, router } = await openReport(onSunday);

    fireEvent.change(screen.getByLabelText(/noche/i), { target: { value: 'friday' } });
    await rendered.fixture.whenStable();

    expect(router.url).toBe('/bar-alfa/staff/reports/friday');
  });

  describe('while the night is on', () => {
    beforeEach(() => vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] }));

    it('says the numbers are live', async () => {
      await openReport(duringSaturday);

      expect(screen.getByText('En vivo')).not.toBeNull();
    });

    it('asks for the numbers again on its own', async () => {
      const { http } = await openReport(duringSaturday);

      vi.advanceTimersByTime(REPORT_REFRESH_MS);
      TestBed.tick();

      http.expectOne(metricsOf('saturday'));
    });
  });

  it('does not ask again for a night that is over', async () => {
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
    const { http } = await openReport(onSunday);

    vi.advanceTimersByTime(REPORT_REFRESH_MS * 2);
    TestBed.tick();

    http.expectNone(metricsOf('saturday'));
  });

  it('says so when the venue has no such night', async () => {
    const { rendered, http } = await openScreen(onSunday, 'saturday');

    http.expectOne(NIGHTS_URL).flush([saturday]);
    http
      .expectOne(metricsOf('saturday'))
      .flush({ type: ProblemTypes.nightNotFound }, { status: 404, statusText: 'Not Found' });
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('No encontramos esa noche');
  });

  it('offers to try again when the report cannot be loaded', async () => {
    const { rendered, http } = await openScreen(onSunday, 'saturday');

    http.expectOne(NIGHTS_URL).flush([saturday]);
    http.expectOne(metricsOf('saturday')).flush('', { status: 500, statusText: 'Server Error' });
    await rendered.fixture.whenStable();

    screen.getByRole('button', { name: /reintentar/i }).click();
    TestBed.tick();

    http.expectOne(metricsOf('saturday'));
  });
});
