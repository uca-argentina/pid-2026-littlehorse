import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { ProblemTypes } from '../../../core/api/problem-types';
import { NIGHTS_URL } from '../nights.service';
import type { Night } from '../nights.service';
import { NightsPage } from './nights.page';

const noAudit = { createdAt: null, createdBy: null, lastModifiedAt: null, lastModifiedBy: null };

const now = new Date(2026, 9, 10, 1, 0);

function aNight(name: string, startsAt: Date, hours = 7): Night {
  return {
    id: `id-${name}`,
    name,
    startsAt: startsAt.toISOString(),
    endsAt: new Date(startsAt.getTime() + hours * 60 * 60 * 1000).toISOString(),
    crewIds: ['kds-1', 'till-1', 'waiter-1'],
    audit: noAudit,
  };
}

// Latest first, as the API sends them.
const theNights: Night[] = [
  aNight('Saturday', new Date(2026, 9, 10, 23, 0)),
  aNight('Friday', new Date(2026, 9, 9, 23, 0)),
  aNight('Thursday', new Date(2026, 9, 8, 23, 0)),
];

async function openScreen() {
  const rendered = await render(NightsPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  });

  return { rendered, http: TestBed.inject(HttpTestingController) };
}

async function openScreenShowing(nights: Night[]) {
  const { rendered, http } = await openScreen();

  http.expectOne(NIGHTS_URL).flush(nights);
  await rendered.fixture.whenStable();

  return rendered;
}

describe('NightsPage', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(now);
  });
  afterEach(() => vi.useRealTimers());

  it('lists the nights of the venue', async () => {
    await openScreenShowing(theNights);

    expect(screen.getByText('Saturday')).not.toBeNull();
    expect(screen.getByText('Friday')).not.toBeNull();
    expect(screen.getByText('Thursday')).not.toBeNull();
  });

  // Not by color alone: the word is what says which one is on.
  it('says which night is on, which is next and which are over', async () => {
    await openScreenShowing(theNights);

    expect(screen.getByText('Saturday').closest('li')?.textContent).toContain('Próxima');
    expect(screen.getByText('Friday').closest('li')?.textContent).toContain('En curso');
    expect(screen.getByText('Thursday').closest('li')?.textContent).toContain('Terminada');
  });

  it('shows the hours and how many people work each night', async () => {
    await openScreenShowing(theNights);

    const friday = screen.getByText('Friday').closest('li')?.textContent ?? '';
    expect(friday).toContain('vie 09/10 · 23:00 a 06:00');
    expect(friday).toContain('3 personas');
  });

  it('draws the outline of the list while it loads', async () => {
    await openScreen();

    expect(screen.getByTestId('nights-skeleton').closest('[aria-busy="true"]')).not.toBeNull();
    expect(screen.getByRole('status').textContent).toContain('Cargando');
  });

  it('says there are no nights yet, and offers to set up the first', async () => {
    await openScreenShowing([]);

    expect(screen.getByRole('status').textContent).toContain('Todavía no armaste ninguna noche');
    expect(screen.getByRole('link', { name: /nueva noche/i }).getAttribute('href')).toBe(
      '/bar-alfa/staff/nights/new',
    );
  });

  it('says so when the listing cannot be loaded', async () => {
    const { rendered, http } = await openScreen();

    http.expectOne(NIGHTS_URL).flush('', { status: 500, statusText: 'Server Error' });
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('No pudimos traer las noches');
  });

  it('offers to try again after a failure, and loads the list when it does', async () => {
    const { rendered, http } = await openScreen();
    http.expectOne(NIGHTS_URL).flush('', { status: 500, statusText: 'Server Error' });
    await rendered.fixture.whenStable();

    // tick and not whenStable: the retried request is pending until this test
    // answers it, so the fixture would never call itself stable.
    screen.getByRole('button', { name: /reintentar/i }).click();
    TestBed.tick();
    http.expectOne(NIGHTS_URL).flush(theNights);
    await rendered.fixture.whenStable();

    expect(screen.getByText('Saturday')).not.toBeNull();
  });

  it('tells apart a refused role from a connection that dropped', async () => {
    const { rendered, http } = await openScreen();

    http
      .expectOne(NIGHTS_URL)
      .flush({ type: ProblemTypes.forbidden }, { status: 403, statusText: 'Forbidden' });
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('Esta pantalla es de administración');
    expect(screen.queryByRole('button', { name: /reintentar/i })).toBeNull();
  });
});
