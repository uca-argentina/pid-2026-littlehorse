import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ProblemTypes } from '../../../core/api/problem-types';
import { render, screen } from '@testing-library/angular';
import { STAFF_USERS_URL } from '../staff-users.service';
import type { StaffUser } from '../staff-users.service';
import { StaffUsersPage } from './staff-users.page';

const theTeam: StaffUser[] = [
  { id: 'id-1', username: 'euge.q', role: 'Administrator', isActive: true },
  { id: 'id-2', username: 'martin.p', role: 'Waiter', isActive: true },
  { id: 'id-3', username: 'pablo.l', role: 'Kds', isActive: false },
];

async function openScreen() {
  const rendered = await render(StaffUsersPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  });

  return { rendered, http: TestBed.inject(HttpTestingController) };
}

async function openScreenShowing(staff: StaffUser[]) {
  const { rendered, http } = await openScreen();

  http.expectOne(STAFF_USERS_URL).flush(staff);
  await rendered.fixture.whenStable();

  return rendered;
}

describe('StaffUsersPage', () => {
  it('lists everyone working at the venue', async () => {
    await openScreenShowing(theTeam);

    expect(screen.getByText('euge.q')).not.toBeNull();
    expect(screen.getByText('martin.p')).not.toBeNull();
    expect(screen.getByText('pablo.l')).not.toBeNull();
  });

  it('shows each role in the language of the venue', async () => {
    await openScreenShowing(theTeam);

    expect(screen.getByText('Administrador')).not.toBeNull();
    expect(screen.getByText('Mozo')).not.toBeNull();
  });

  // US-05 keeps deactivated staff in the listing so their orders still point
  // somewhere. Showing them identical to everyone else would be worse than
  // hiding them: the administrator would think that person can still sign in.
  it('marks whoever was deactivated instead of hiding them', async () => {
    await openScreenShowing(theTeam);

    expect(screen.getByText(/dado de baja/i)).not.toBeNull();
  });

  it('asks the API without naming a venue, because the token carries it', async () => {
    const { http } = await openScreen();

    expect(http.expectOne(STAFF_USERS_URL).request.method).toBe('GET');
  });

  // The venue's network is saturated at midnight. An empty screen with no
  // explanation is indistinguishable from a venue with no staff.
  it('says so when the listing cannot be loaded', async () => {
    const { rendered, http } = await openScreen();

    http.expectOne(STAFF_USERS_URL).flush('', { status: 500, statusText: 'Server Error' });
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('No pudimos');
  });

  /**
   * The guard keeps this screen shut for everyone but an administrator, so a 403
   * here means the role changed underneath an open tab. Telling them to check
   * the signal would send them chasing a network that is perfectly fine.
   */
  it('tells apart a refused role from a connection that dropped', async () => {
    const { rendered, http } = await openScreen();

    http
      .expectOne(STAFF_USERS_URL)
      .flush({ type: ProblemTypes.forbidden }, { status: 403, statusText: 'Forbidden' });
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('Esta pantalla es de administración');
  });

  it('offers the way to add somebody', async () => {
    await openScreenShowing(theTeam);

    expect(screen.getByRole('link', { name: /nuevo usuario/i })).not.toBeNull();
  });
});
