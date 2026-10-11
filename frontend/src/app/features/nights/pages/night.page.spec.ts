import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { of } from 'rxjs';
import { ProblemTypes } from '../../../core/api/problem-types';
import { STAFF_USERS_URL } from '../../staff-users/staff-users.service';
import type { StaffUser } from '../../staff-users/staff-users.service';
import { NIGHTS_URL, NightsService, nightStockUrl } from '../nights.service';
import type { Night, NightStockLine } from '../nights.service';
import { NightPage } from './night.page';

const noAudit = { createdAt: null, createdBy: null, lastModifiedAt: null, lastModifiedBy: null };

const theTeam: StaffUser[] = [
  { id: 'main-bar', username: 'main-bar', role: 'Kds', isActive: true, audit: noAudit },
  { id: 'till-1', username: 'till-1', role: 'Cashier', isActive: true, audit: noAudit },
  { id: 'martin', username: 'martin', role: 'Waiter', isActive: true, audit: noAudit },
];

const saturday: Night = {
  id: 'night-1',
  name: 'Saturday',
  startsAt: new Date(2026, 9, 10, 23, 0).toISOString(),
  endsAt: new Date(2026, 9, 11, 6, 0).toISOString(),
  crewIds: ['main-bar', 'till-1'],
  audit: noAudit,
};

const theStock: NightStockLine[] = [
  { productId: 'gin', productName: 'Gin Tonic', loaded: 20, sold: 3, remaining: 17 },
];

const beforeItStarts = new Date(2026, 9, 10, 20, 0);
const whileItIsOn = new Date(2026, 9, 11, 1, 0);
const afterItEnded = new Date(2026, 9, 11, 7, 0);

async function openScreen(now: Date, update = vi.fn().mockReturnValue(of(saturday))) {
  vi.setSystemTime(now);

  const rendered = await render(NightPage, {
    inputs: { venueSlug: 'bar-alfa', id: 'night-1' },
    providers: [
      provideRouter([{ path: ':venueSlug/staff/nights', children: [] }]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: NightsService, useValue: { update } },
    ],
  });

  return { rendered, update, http: TestBed.inject(HttpTestingController) };
}

/** Renders what the last answer changed and lets its effects run, without waiting on any request. */
async function settleOnce(fixture: { detectChanges: () => void }): Promise<void> {
  for (let round = 0; round < 3; round++) {
    fixture.detectChanges();
    TestBed.tick();
    await Promise.resolve();
  }
}

async function openScreenShowing(
  night: Night,
  now: Date,
  update?: Parameters<typeof openScreen>[1],
) {
  const opened = await openScreen(now, update);

  opened.http.expectOne(`${NIGHTS_URL}/night-1`).flush(night);
  opened.http.expectOne(STAFF_USERS_URL).flush(theTeam);
  // The stock is asked for once the night is on screen (US-37). The request
  // is pending until it is answered, and a pending request is what keeps
  // whenStable() from returning, so it is answered before waiting.
  await settleOnce(opened.rendered.fixture);
  opened.http.expectOne(nightStockUrl('night-1')).flush(theStock);
  await opened.rendered.fixture.whenStable();

  return opened;
}

describe('NightPage', () => {
  beforeEach(() => vi.useFakeTimers({ toFake: ['Date'] }));
  afterEach(() => vi.useRealTimers());

  it('opens the saved night ready to edit before it starts', async () => {
    await openScreenShowing(saturday, beforeItStarts);

    expect(screen.getByRole('heading', { name: 'Saturday' })).not.toBeNull();
    expect((screen.getByLabelText(/nombre/i) as HTMLInputElement).value).toBe('Saturday');
    expect((screen.getByLabelText(/empieza/i) as HTMLInputElement).disabled).toBe(false);
    expect(screen.getByText('Próxima')).not.toBeNull();
  });

  it('saves the edit through the API and goes back to the listing', async () => {
    const { rendered, update } = await openScreenShowing(saturday, beforeItStarts);

    screen.getByRole('button', { name: /guardar cambios/i }).click();
    await rendered.fixture.whenStable();

    expect(update).toHaveBeenCalledWith('night-1', {
      name: 'Saturday',
      startsAt: saturday.startsAt,
      endsAt: saturday.endsAt,
      crewIds: ['main-bar', 'till-1'],
    });
  });

  // Its orders already belong to it from its start.
  it('locks the start of a night that is on', async () => {
    await openScreenShowing(saturday, whileItIsOn);

    expect(screen.getByText('En curso')).not.toBeNull();
    expect((screen.getByLabelText(/empieza/i) as HTMLInputElement).disabled).toBe(true);
  });

  // Its metrics are read against those hours, so it is shown, not edited.
  it('shows a finished night with its crew, and nothing to save', async () => {
    await openScreenShowing(saturday, afterItEnded);

    expect(screen.getByText('Terminada')).not.toBeNull();
    expect(screen.getByText('main-bar')).not.toBeNull();
    expect(screen.getByText('till-1')).not.toBeNull();
    expect(screen.queryByRole('button', { name: /guardar/i })).toBeNull();
    expect(screen.queryByLabelText(/nombre/i)).toBeNull();
  });

  // US-37, criterion 1: the stock is loaded from the night's own screen, before
  // it starts and while it is on.
  it('shows the stock of the night under its form, with a way to move it', async () => {
    await openScreenShowing(saturday, beforeItStarts);

    expect(screen.getByRole('heading', { name: /stock de la noche/i })).not.toBeNull();
    expect(screen.getByRole('listitem', { name: /gin tonic/i })).not.toBeNull();
    expect(screen.getByRole('spinbutton')).not.toBeNull();
  });

  // Criterion 3: a night that ended is read, never moved.
  it('shows the stock of a finished night without a way to move it', async () => {
    await openScreenShowing(saturday, afterItEnded);

    expect(screen.getByRole('listitem', { name: /gin tonic/i })).not.toBeNull();
    expect(screen.queryByRole('spinbutton')).toBeNull();
  });

  it('says so when the venue has no such night', async () => {
    const { rendered, http } = await openScreen(beforeItStarts);

    http
      .expectOne(`${NIGHTS_URL}/night-1`)
      .flush({ type: ProblemTypes.nightNotFound }, { status: 404, statusText: 'Not Found' });
    http.expectOne(STAFF_USERS_URL).flush(theTeam);
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('No encontramos esa noche');
    expect(screen.queryByRole('button', { name: /reintentar/i })).toBeNull();
  });

  it('offers to try again when the night cannot be loaded', async () => {
    const { rendered, http } = await openScreen(beforeItStarts);

    http.expectOne(`${NIGHTS_URL}/night-1`).flush('', { status: 500, statusText: 'Server Error' });
    http.expectOne(STAFF_USERS_URL).flush(theTeam);
    await rendered.fixture.whenStable();

    screen.getByRole('button', { name: /reintentar/i }).click();
    TestBed.tick();
    http.expectOne(`${NIGHTS_URL}/night-1`).flush(saturday);
    await settleOnce(rendered.fixture);
    http.expectOne(nightStockUrl('night-1')).flush(theStock);
    await rendered.fixture.whenStable();

    expect((screen.getByLabelText(/nombre/i) as HTMLInputElement).value).toBe('Saturday');
  });
});
