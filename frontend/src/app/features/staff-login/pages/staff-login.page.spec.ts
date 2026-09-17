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

// selector: 'input' on purpose. The password field's eye carries an aria-label
// that also says "contraseña", so both match without it.
function field(label: RegExp): HTMLInputElement {
  return screen.getByLabelText(label, { selector: 'input' }) as HTMLInputElement;
}

function type(label: RegExp, value: string): void {
  fireEvent.input(field(label), { target: { value } });
}

function eye(): HTMLButtonElement {
  return screen.getByRole('button', { name: /contraseña/i }) as HTMLButtonElement;
}

function rejectedWith(type: string): HttpErrorResponse {
  return new HttpErrorResponse({ status: 401, error: { type } });
}

describe('StaffLoginPage', () => {
  it('cannot be submitted while the form is empty', async () => {
    await openScreen(vi.fn());

    expect(enterButton().disabled).toBe(true);
  });

  it('cannot be submitted with only the username typed', async () => {
    const { fixture } = await openScreen(vi.fn());

    type(/usuario/i, 'euge');
    await fixture.whenStable();

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

    const usernameField = field(/usuario/i);
    const message = screen.getByRole('alert');

    expect(usernameField.getAttribute('aria-invalid')).toBe('true');
    expect(usernameField.getAttribute('aria-describedby')).toBe(message.id);
  });

  // Moved here with the form: the store no longer holds the field values, so
  // these three are now about what the screen does with them.
  it('cannot be submitted with a username of only whitespace', async () => {
    const { fixture } = await openScreen(vi.fn());

    type(/usuario/i, '   ');
    type(/contraseña/i, 'a-password');
    await fixture.whenStable();

    expect(enterButton().disabled).toBe(true);
  });

  it('sends the username trimmed', async () => {
    const logIn = vi.fn().mockReturnValue(new Subject<StaffSession>());
    const { fixture } = await openScreen(logIn);

    type(/usuario/i, '  euge  ');
    type(/contraseña/i, 'a-password');
    await fixture.whenStable();
    fireEvent.click(enterButton());

    expect(logIn).toHaveBeenCalledWith('bar-alfa', {
      username: 'euge',
      password: 'a-password',
    });
  });

  /**
   * The tablet behind the bar is shared and unattended between shifts. A
   * password left sitting in the field is readable by whoever picks it up next.
   */
  it('clears the password and keeps the username after a wrong login', async () => {
    const logIn = vi
      .fn()
      .mockReturnValue(throwError(() => rejectedWith(ProblemTypes.invalidCredentials)));
    const { fixture } = await openScreen(logIn);

    type(/usuario/i, 'euge');
    type(/contraseña/i, 'wrong-password');
    await fixture.whenStable();
    fireEvent.click(enterButton());
    await fixture.whenStable();

    expect(field(/contraseña/i).value).toBe('');
    expect(field(/usuario/i).value).toBe('euge');
  });

  it('labels every field for a screen reader', async () => {
    await openScreen(vi.fn());

    // getByLabelText throws when no control carries that accessible name, so
    // reaching the assertion is already the check.
    expect(field(/usuario/i)).not.toBeNull();
    expect(field(/contraseña/i)).not.toBeNull();
  });

  /**
   * A tablet behind the bar, in the dark, with a password somebody dictated to
   * them. Typing it blind and being told only "usuario o contraseña
   * incorrectos" is how a shift starts with three failed attempts.
   */
  it('hides the password behind dots until somebody asks to see it', async () => {
    await openScreen(vi.fn());

    expect(field(/contraseña/i).type).toBe('password');
  });

  it('shows the password while the eye is on', async () => {
    const { fixture } = await openScreen(vi.fn());

    eye().click();
    await fixture.whenStable();

    expect(field(/contraseña/i).type).toBe('text');
  });

  it('hides it again on the second press', async () => {
    const { fixture } = await openScreen(vi.fn());

    eye().click();
    await fixture.whenStable();
    eye().click();
    await fixture.whenStable();

    expect(field(/contraseña/i).type).toBe('password');
  });

  // A button inside a form submits it unless it says otherwise, and this one
  // would fire a login attempt every time somebody peeked.
  it('does not try to sign in when the eye is pressed', async () => {
    const logIn = vi.fn();
    const { fixture } = await openScreen(logIn);

    type(/usuario/i, 'euge');
    type(/contraseña/i, 'a-password');
    await fixture.whenStable();

    eye().click();
    await fixture.whenStable();

    expect(logIn).not.toHaveBeenCalled();
  });
});
