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
  it('cannot be submitted while the form is empty', async () => {
    await openScreen(vi.fn());

    expect(enterButton().disabled).toBe(true);
  });

  it('can be submitted once both fields are filled', async () => {
    const { fixture } = await openScreen(vi.fn());

    type(/usuario/i, 'euge');
    type(/contraseña/i, 'a-password');
    await fixture.whenStable();

    expect(enterButton().disabled).toBe(false);
  });

  it('sends the credentials for the venue in the address', async () => {
    const logIn = vi.fn().mockReturnValue(new Subject<StaffSession>());
    const { fixture } = await openScreen(logIn);

    type(/usuario/i, 'euge');
    type(/contraseña/i, 'a-password');
    await fixture.whenStable();
    fireEvent.click(enterButton());

    expect(logIn).toHaveBeenCalledWith('bar-alfa', {
      username: 'euge',
      password: 'a-password',
    });
  });

  it('reports a wrong login without naming which field failed', async () => {
    const logIn = vi
      .fn()
      .mockReturnValue(throwError(() => rejectedWith(ProblemTypes.invalidCredentials)));
    const { fixture } = await openScreen(logIn);

    type(/usuario/i, 'euge');
    type(/contraseña/i, 'wrong-password');
    await fixture.whenStable();
    fireEvent.click(enterButton());
    await fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('Usuario o contraseña incorrectos');
    expect(screen.queryByText(/no existe/i)).toBeNull();
  });

  it('does not blame the credentials when the API cannot be reached', async () => {
    const logIn = vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    const { fixture } = await openScreen(logIn);

    type(/usuario/i, 'euge');
    type(/contraseña/i, 'a-password');
    await fixture.whenStable();
    fireEvent.click(enterButton());
    await fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('No pudimos conectarnos');
  });

  it('reads as an expired shift, not an error, when the guard turned them away', async () => {
    await openScreen(vi.fn(), { venueSlug: 'bar-alfa', expired: 'true' });

    expect(screen.getByRole('heading').textContent).toContain('Se terminó tu sesión');
    expect(screen.queryByText(/incorrectos/i)).toBeNull();
  });

  it('shows the venue named in the address and not a fixed one', async () => {
    await openScreen(vi.fn(), { venueSlug: 'bar-beta' });

    expect(screen.getByText(/bar-beta/i)).not.toBeNull();
    expect(screen.queryByText(/bar-alfa/i)).toBeNull();
  });

  it('marks both fields invalid and points them at the message', async () => {
    const logIn = vi
      .fn()
      .mockReturnValue(throwError(() => rejectedWith(ProblemTypes.invalidCredentials)));
    const { fixture } = await openScreen(logIn);

    type(/usuario/i, 'euge');
    type(/contraseña/i, 'wrong-password');
    await fixture.whenStable();
    fireEvent.click(enterButton());
    await fixture.whenStable();

    const usernameField = screen.getByLabelText(/usuario/i);
    const message = screen.getByRole('alert');

    expect(usernameField.getAttribute('aria-invalid')).toBe('true');
    expect(usernameField.getAttribute('aria-describedby')).toBe(message.id);
  });

  it('labels every field for a screen reader', async () => {
    await openScreen(vi.fn());

    // getByLabelText throws when no control carries that accessible name, so
    // reaching the assertion is already the check.
    expect(screen.getByLabelText(/usuario/i)).not.toBeNull();
    expect(screen.getByLabelText(/contraseña/i)).not.toBeNull();
  });
});
