import { HttpErrorResponse } from '@angular/common/http';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { fireEvent, screen } from '@testing-library/angular';
import { throwError } from 'rxjs';
import { ProblemTypes } from '../../core/api/problem-types';
import { SessionStorage } from '../../core/auth/session-storage';
import { tablesRoutes } from './tables.routes';
import { TablesService } from './tables.service';

/**
 * Through the real routes, because the page specs cannot see this: they hand
 * the store to render() themselves, so every test gets a fresh one. What the
 * administrator gets is whatever the route's injector hands out — and Angular
 * creates that injector once per route config and keeps it.
 */
describe('tablesRoutes', () => {
  async function openAdministration(role: 'Administrator' | 'Cashier' = 'Administrator') {
    TestBed.configureTestingModule({
      providers: [
        // As in app.config.ts: it is how venueSlug reaches the page.
        provideRouter(
          [
            { path: ':venueSlug/staff/tables', children: tablesRoutes },
            { path: ':venueSlug/staff/login', children: [] },
            { path: ':venueSlug/staff', children: [] },
          ],
          withComponentInputBinding(),
        ),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: TablesService,
          useValue: {
            create: vi.fn().mockReturnValue(
              throwError(
                () =>
                  new HttpErrorResponse({
                    status: 409,
                    error: { type: ProblemTypes.tableNumberTaken },
                  }),
              ),
            ),
          },
        },
      ],
    });

    TestBed.inject(SessionStorage).remember({
      token: 'a-token',
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
      username: 'euge',
      displayName: null,
      role,
    });

    return RouterTestingHarness.create();
  }

  // The bug as it was found in the staff form: create something that is taken,
  // see the message, cancel, come back to create another — and the message is
  // still there over an empty form.
  it('opens the new-table form clean after a rejected attempt and a trip to the listing', async () => {
    const harness = await openAdministration();

    await harness.navigateByUrl('/bar-alfa/staff/tables/new');
    fireEvent.input(screen.getByLabelText(/^número/i), { target: { value: '5' } });
    fireEvent.input(screen.getByLabelText(/^personas/i), { target: { value: '8' } });
    screen.getByRole('button', { name: /crear mesa/i }).click();
    await harness.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('Ya hay una mesa');

    await harness.navigateByUrl('/bar-alfa/staff/tables');
    await harness.navigateByUrl('/bar-alfa/staff/tables/new');
    await harness.fixture.whenStable();

    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('keeps somebody who is not an administrator out of the listing', async () => {
    const harness = await openAdministration('Cashier');

    await harness.navigateByUrl('/bar-alfa/staff/tables');

    expect(TestBed.inject(Router).url).not.toBe('/bar-alfa/staff/tables');
    expect(screen.queryByRole('heading', { name: /mesas vip/i })).toBeNull();
  });
});
