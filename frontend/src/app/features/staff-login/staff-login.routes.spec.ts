import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { fireEvent, screen } from '@testing-library/angular';
import { throwError } from 'rxjs';
import { ProblemTypes } from '../../core/api/problem-types';
import { staffLoginRoutes } from './staff-login.routes';
import { StaffLoginService } from './staff-login.service';

/**
 * Through the real routes, for the same reason as staffUsersRoutes.spec: the
 * page specs provide the store themselves, so they cannot see what the route's
 * injector — created once and kept — would hand to the next visit.
 */
describe('staffLoginRoutes', () => {
  async function openLogin(): Promise<RouterTestingHarness> {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(
          [
            { path: ':venueSlug/staff/login', children: staffLoginRoutes },
            // Somewhere else to go: the installed PWA's entry screen, say.
            { path: '', children: [] },
          ],
          withComponentInputBinding(),
        ),
        {
          provide: StaffLoginService,
          useValue: {
            logIn: vi.fn().mockReturnValue(
              throwError(
                () =>
                  new HttpErrorResponse({
                    status: 401,
                    error: { type: ProblemTypes.invalidCredentials },
                  }),
              ),
            ),
          },
        },
      ],
    });

    return RouterTestingHarness.create();
  }

  function type(label: RegExp, value: string): void {
    fireEvent.input(screen.getByLabelText(label), { target: { value } });
  }

  it('opens the login clean after a rejected attempt and a trip elsewhere', async () => {
    const harness = await openLogin();

    await harness.navigateByUrl('/bar-alfa/staff/login');
    type(/usuario/i, 'euge');
    type(/contraseña/i, 'wrong-password');
    await harness.fixture.whenStable();
    screen.getByRole('button', { name: /entrar/i }).click();
    await harness.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('incorrectos');

    await harness.navigateByUrl('/');
    await harness.navigateByUrl('/bar-alfa/staff/login');
    await harness.fixture.whenStable();

    expect(screen.queryByRole('alert')).toBeNull();
  });
});
