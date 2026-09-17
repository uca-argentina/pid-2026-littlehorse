import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ProblemTypes } from '../../../core/api/problem-types';
import { fireEvent, render, screen } from '@testing-library/angular';
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

  // US-04: the way in to correcting somebody is their own row, not an address
  // an administrator has to be told.
  it('offers the way to correct each person', async () => {
    await openScreenShowing(theTeam);

    const editing = screen.getAllByRole('link', { name: /editar/i });

    expect(editing).toHaveLength(theTeam.length);
    expect(editing[0].getAttribute('href')).toBe('/bar-alfa/staff/users/id-1');
  });

  it('offers the way to add somebody', async () => {
    await openScreenShowing(theTeam);

    expect(screen.getByRole('link', { name: /nuevo usuario/i })).not.toBeNull();
  });

  describe('searching and filtering', () => {
    function search(term: string): void {
      fireEvent.input(screen.getByLabelText(/buscar/i, { selector: 'input' }), {
        target: { value: term },
      });
    }

    function pill(name: RegExp): HTMLButtonElement {
      return screen.getByRole('button', { name }) as HTMLButtonElement;
    }

    function listed(): string[] {
      return screen
        .getAllByRole('listitem')
        .map((row) => row.textContent?.trim().split(/\s+/)[0] ?? '');
    }

    it('narrows the list to whoever matches what was typed', async () => {
      const rendered = await openScreenShowing(theTeam);

      search('mar');
      await rendered.fixture.whenStable();

      expect(listed()).toEqual(['martin.p']);
    });

    // Usernames are stored lowercase, and nobody types them that way in a hurry.
    it('matches however it was capitalised', async () => {
      const rendered = await openScreenShowing(theTeam);

      search('MARTIN');
      await rendered.fixture.whenStable();

      expect(listed()).toEqual(['martin.p']);
    });

    // Not the same situation as a venue with nobody loaded, and saying the wrong
    // one sends an administrator looking for a bug that is not there.
    it('says nothing matched, which is not an empty venue', async () => {
      const rendered = await openScreenShowing(theTeam);

      search('zzz');
      await rendered.fixture.whenStable();

      expect(screen.getByRole('status').textContent).toContain('Ningún usuario coincide');
      expect(screen.queryByText(/todavía no hay nadie cargado/i)).toBeNull();
    });

    it('counts how many people each role has', async () => {
      await openScreenShowing(theTeam);

      expect(pill(/todos/i).textContent).toContain('3');
      expect(pill(/administradores/i).textContent).toContain('1');
      expect(pill(/mozos/i).textContent).toContain('1');
    });

    it('shows one role alone when its filter is on', async () => {
      const rendered = await openScreenShowing(theTeam);

      pill(/mozos/i).click();
      await rendered.fixture.whenStable();

      expect(listed()).toEqual(['martin.p']);
    });

    // Deactivated staff are part of the venue's team, so a role filter has to
    // keep them: they are exactly who an administrator goes looking for.
    it('keeps whoever was deactivated inside their role', async () => {
      const rendered = await openScreenShowing(theTeam);

      pill(/KDS/i).click();
      await rendered.fixture.whenStable();

      expect(listed()).toEqual(['pablo.l']);
    });

    it('narrows by role and by what was typed at once', async () => {
      const rendered = await openScreenShowing(theTeam);

      pill(/mozos/i).click();
      search('pab');
      await rendered.fixture.whenStable();

      expect(screen.getByRole('status').textContent).toContain('Ningún usuario coincide');
    });

    // A count that ignores the search sends somebody to a pill that then shows
    // nothing, which reads as a broken filter.
    it('counts what the search left, not the whole venue', async () => {
      const rendered = await openScreenShowing(theTeam);

      search('mar');
      await rendered.fixture.whenStable();

      expect(pill(/todos/i).textContent).toContain('1');
      expect(pill(/administradores/i).textContent).toContain('0');
    });

    it('goes back to everyone', async () => {
      const rendered = await openScreenShowing(theTeam);

      pill(/mozos/i).click();
      await rendered.fixture.whenStable();
      pill(/todos/i).click();
      await rendered.fixture.whenStable();

      expect(listed()).toEqual(['euge.q', 'martin.p', 'pablo.l']);
    });

    it('says which filter is on, for a screen reader too', async () => {
      const rendered = await openScreenShowing(theTeam);

      pill(/mozos/i).click();
      await rendered.fixture.whenStable();

      expect(pill(/mozos/i).getAttribute('aria-pressed')).toBe('true');
      expect(pill(/todos/i).getAttribute('aria-pressed')).toBe('false');
    });
  });
});
