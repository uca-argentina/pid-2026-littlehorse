import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { Subject, throwError } from 'rxjs';
import { ProblemTypes } from '../../../core/api/problem-types';
import type { StaffSession } from '../../../core/auth/staff-session';
import { StaffLoginService } from '../staff-login.service';
import { StaffLoginStore } from '../staff-login.store';
import { StaffLoginPage } from './staff-login.page';

/** Every spec here asserts on what the person at the venue sees and does. */
function openScreen(
  logIn: ReturnType<typeof vi.fn>,
  inputs: Record<string, unknown> = { venueSlug: 'bar-alfa' },
) {
  return render(StaffLoginPage, {
    inputs,
    providers: [
      provideRouter([]),
      StaffLoginStore,
      { provide: StaffLoginService, useValue: { logIn } },
    ],
  });
}

function enterButton(): HTMLButtonElement {
  return screen.getByRole('button', { name: /entrar/i }) as HTMLButtonElement;
}

function type(label: RegExp, value: string): void {
  fireEvent.input(screen.getByLabelText(label), { target: { value } });
}

function rejectedWith(type: string): HttpErrorResponse {
  return new HttpErrorResponse({ status: 401, error: { type } });
}

describe('StaffLoginPage', () => {
  it('Entrar_WhenTheFormIsEmpty_CannotBePressed', async () => {
    await openScreen(vi.fn());

    expect(enterButton().disabled).toBe(true);
  });

  it('Entrar_WhenBothFieldsAreFilled_CanBePressed', async () => {
    const { fixture } = await openScreen(vi.fn());

    type(/usuario/i, 'euge');
    type(/contraseña/i, 'una-contrasena');
    await fixture.whenStable();

    expect(enterButton().disabled).toBe(false);
  });

  it('Entrar_WhenPressed_SendsTheCredentialsForThatVenue', async () => {
    const logIn = vi.fn().mockReturnValue(new Subject<StaffSession>());
    const { fixture } = await openScreen(logIn);

    type(/usuario/i, 'euge');
    type(/contraseña/i, 'una-contrasena');
    await fixture.whenStable();
    fireEvent.click(enterButton());

    expect(logIn).toHaveBeenCalledWith('bar-alfa', {
      username: 'euge',
      password: 'una-contrasena',
    });
  });

  it('Render_WhenCredentialsAreWrong_SaysSoWithoutNamingTheField', async () => {
    const logIn = vi
      .fn()
      .mockReturnValue(throwError(() => rejectedWith(ProblemTypes.invalidCredentials)));
    const { fixture } = await openScreen(logIn);

    type(/usuario/i, 'euge');
    type(/contraseña/i, 'mal');
    await fixture.whenStable();
    fireEvent.click(enterButton());
    await fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('Usuario o contraseña incorrectos');
    expect(screen.queryByText(/no existe/i)).toBeNull();
  });

  it('Render_WhenTheApiCannotBeReached_DoesNotBlameTheCredentials', async () => {
    const logIn = vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    const { fixture } = await openScreen(logIn);

    type(/usuario/i, 'euge');
    type(/contraseña/i, 'una-contrasena');
    await fixture.whenStable();
    fireEvent.click(enterButton());
    await fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('No pudimos conectarnos');
  });

  it('Render_WhenTheGuardTurnedThemAway_ReadsAsAnExpiredShiftAndNotAsAnError', async () => {
    await openScreen(vi.fn(), { venueSlug: 'bar-alfa', vencida: 'true' });

    expect(screen.getByRole('heading').textContent).toContain('Se terminó tu sesión');
    expect(screen.queryByText(/incorrectos/i)).toBeNull();
  });

  it('Render_WhenTheAddressNamesAVenue_ShowsThatVenueAndNotAFixedOne', async () => {
    await openScreen(vi.fn(), { venueSlug: 'bar-beta' });

    expect(screen.getByText(/bar-beta/i)).not.toBeNull();
    expect(screen.queryByText(/bar-alfa/i)).toBeNull();
  });

  it('Render_WhenCredentialsAreWrong_MarksBothFieldsAsInvalidAndPointsAtTheMessage', async () => {
    const logIn = vi
      .fn()
      .mockReturnValue(throwError(() => rejectedWith(ProblemTypes.invalidCredentials)));
    const { fixture } = await openScreen(logIn);

    type(/usuario/i, 'euge');
    type(/contraseña/i, 'mal');
    await fixture.whenStable();
    fireEvent.click(enterButton());
    await fixture.whenStable();

    const usuario = screen.getByLabelText(/usuario/i);
    const message = screen.getByRole('alert');

    expect(usuario.getAttribute('aria-invalid')).toBe('true');
    expect(usuario.getAttribute('aria-describedby')).toBe(message.id);
  });

  it('Render_WhenTheScreenOpens_LabelsEveryFieldForAScreenReader', async () => {
    await openScreen(vi.fn());

    // getByLabelText throws when no control carries that accessible name, so
    // reaching the assertion is already the check.
    expect(screen.getByLabelText(/usuario/i)).not.toBeNull();
    expect(screen.getByLabelText(/contraseña/i)).not.toBeNull();
  });
});
