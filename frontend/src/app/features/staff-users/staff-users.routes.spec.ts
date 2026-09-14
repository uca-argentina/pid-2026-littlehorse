import { HttpErrorResponse } from '@angular/common/http';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { fireEvent, screen } from '@testing-library/angular';
import { throwError } from 'rxjs';
import { ProblemTypes } from '../../core/api/problem-types';
import { SessionStorage } from '../../core/auth/session-storage';
import { StaffUsersService } from './staff-users.service';
import { staffUsersRoutes } from './staff-users.routes';

/**
 * Through the real routes, because the page specs cannot see this: they hand
 * the store to render() themselves, so every test gets a fresh one. What the
 * administrator gets is whatever the route's injector hands out — and Angular
 * creates that injector once per route config and keeps it.
 */
describe('staffUsersRoutes', () => {
  async function openAdministration(): Promise<RouterTestingHarness> {
    TestBed.configureTestingModule({
      providers: [
        // As in app.config.ts: it is how venueSlug reaches the page.
        provideRouter(
          [{ path: ':venueSlug/staff/users', children: staffUsersRoutes }],
          withComponentInputBinding(),
        ),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: StaffUsersService,
          useValue: {
            create: vi.fn().mockReturnValue(
              throwError(
                () =>
                  new HttpErrorResponse({
                    status: 409,
                    error: { type: ProblemTypes.usernameTaken },
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
      role: 'Administrator',
    });

    return RouterTestingHarness.create();
  }

  function type(label: RegExp, value: string): void {
    fireEvent.input(screen.getByLabelText(label), { target: { value } });
  }

  // The bug as it was found: create somebody whose username is taken, see the
  // message, cancel, come back to create somebody else — and the message is
  // still there over an empty form.
  it('opens the new-user form clean after a rejected attempt and a trip to the listing', async () => {
    const harness = await openAdministration();

    await harness.navigateByUrl('/bar-alfa/staff/users/new');
    type(/usuario/i, 'martin.p');
    type(/contraseña/i, 'a long enough one');
    fireEvent.click(screen.getByRole('radio', { name: /KDS/i }));
    screen.getByRole('button', { name: /crear usuario/i }).click();
    await harness.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('Ya hay alguien');

    await harness.navigateByUrl('/bar-alfa/staff/users');
    await harness.navigateByUrl('/bar-alfa/staff/users/new');
    await harness.fixture.whenStable();

    expect(screen.queryByRole('alert')).toBeNull();
  });
});
