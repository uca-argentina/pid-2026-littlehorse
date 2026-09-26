import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { Subject, of, throwError } from 'rxjs';
import { ProblemTypes } from '../../../core/api/problem-types';
import { NewStaffUserStore } from '../new-staff-user.store';
import { StaffUsersService } from '../staff-users.service';
import type { StaffUser } from '../staff-users.service';
import { NewStaffUserPage } from './new-staff-user.page';

const noAudit = { createdAt: null, createdBy: null, lastModifiedAt: null, lastModifiedBy: null };

const created: StaffUser = {
  id: 'id-1',
  username: 'martin.p',
  role: 'Waiter',
  isActive: true,
  audit: noAudit,
};

const rejectedWith = (status: number, type: string) =>
  vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status, error: { type } })));

function openScreen(create = vi.fn().mockReturnValue(of(created))) {
  return render(NewStaffUserPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [
      // The screen navigates back to the listing for real: without a route to
      // land on, the router rejects and that masks the actual assertion.
      provideRouter([{ path: ':venueSlug/staff/users', children: [] }]),
      NewStaffUserStore,
      { provide: StaffUsersService, useValue: { create } },
    ],
  }).then((rendered) => ({ rendered, create }));
}

// selector: 'input' on purpose. The eye carries an aria-label that also says
// "contraseña", so without it the password field and its button both match.
function field(label: RegExp): HTMLInputElement {
  return screen.getByLabelText(label, { selector: 'input' }) as HTMLInputElement;
}

function type(label: RegExp, value: string): void {
  fireEvent.input(field(label), { target: { value } });
}

function role(name: RegExp): HTMLInputElement {
  return screen.getByRole('radio', { name }) as HTMLInputElement;
}

function save(): void {
  screen.getByRole('button', { name: /crear usuario/i }).click();
}

function eye(): HTMLButtonElement {
  return screen.getByRole('button', { name: /contraseña/i }) as HTMLButtonElement;
}

describe('NewStaffUserPage', () => {
  // Criterion 2. Not a free-text field: a role the API does not know would come
  // back rejected after a round trip the administrator waited for.
  it('offers the three roles a venue hands out', async () => {
    await openScreen();

    expect(role(/administrador/i)).not.toBeNull();
    expect(role(/KDS/i)).not.toBeNull();
    expect(role(/mozo/i)).not.toBeNull();
  });

  // The least privileged one. Somebody who skips this field must not end up
  // handing out the account that can change the venue's data.
  // Picking the role is the one real decision on this screen, so nothing is
  // picked for them: a default of "the least privileged" was handing out mozo,
  // which has no screen yet, to anyone who skipped the field.
  it('starts with no role picked', async () => {
    await openScreen();

    expect(role(/administrador/i).checked).toBe(false);
    expect(role(/KDS/i).checked).toBe(false);
    expect(role(/mozo/i).checked).toBe(false);
  });

  it('refuses to save until a role is picked, and says so', async () => {
    const { rendered, create } = await openScreen();

    type(/usuario/i, 'martin.p');
    type(/contraseña/i, 'a long enough one');
    save();
    await rendered.fixture.whenStable();

    expect(create).not.toHaveBeenCalled();
    expect(screen.getByText(/elegí un rol/i)).not.toBeNull();
  });

  // Criterion 5, the four ways to get it wrong. Caught here and not by the API:
  // the venue's connection is the slowest part of this screen.
  it.each([
    ['nothing at all', '', ''],
    ['a username of two characters', 'eu', 'a long enough one'],
    ['a username that is only spaces', '    ', 'a long enough one'],
    ['no password', 'martin.p', ''],
    ['a password under eight characters', 'martin.p', 'corta'],
  ])('refuses to save with %s', async (_case, username, password) => {
    const { create } = await openScreen();

    type(/usuario/i, username);
    type(/contraseña/i, password);
    save();

    expect(create).not.toHaveBeenCalled();
  });

  it('says what is wrong instead of only refusing', async () => {
    const { rendered } = await openScreen();

    type(/usuario/i, 'eu');
    type(/contraseña/i, 'a long enough one');
    save();
    await rendered.fixture.whenStable();

    expect(screen.getByText(/al menos tres caracteres/i)).not.toBeNull();
  });

  it('says a short password is short, next to the password', async () => {
    const { rendered } = await openScreen();

    type(/usuario/i, 'martin.p');
    type(/contraseña/i, 'corta');
    save();
    await rendered.fixture.whenStable();

    expect(screen.getByText(/al menos ocho caracteres/i)).not.toBeNull();
  });

  // Both fields wrong at once is the ordinary case, not a corner one: whoever
  // rushes the form gets both messages, fixes one, and has to see that one go.
  it('clears the message of the field that was fixed and keeps the other', async () => {
    const { rendered } = await openScreen();

    type(/usuario/i, 'eu');
    type(/contraseña/i, 'corta');
    save();
    await rendered.fixture.whenStable();

    type(/usuario/i, 'martin.p');
    await rendered.fixture.whenStable();

    expect(screen.queryByText(/al menos tres caracteres/i)).toBeNull();
    expect(screen.getByText(/al menos ocho caracteres/i)).not.toBeNull();
  });

  it('stops calling the field wrong once it is right, for a screen reader too', async () => {
    const { rendered } = await openScreen();

    type(/usuario/i, 'eu');
    type(/contraseña/i, 'corta');
    save();
    await rendered.fixture.whenStable();

    type(/usuario/i, 'martin.p');
    await rendered.fixture.whenStable();

    expect(field(/usuario/i).getAttribute('aria-invalid')).toBeNull();
    expect(field(/contraseña/i).getAttribute('aria-invalid')).toBe('true');
  });

  it('sends what was typed, without the spaces around it', async () => {
    const { create } = await openScreen();

    type(/usuario/i, '  Martin.P  ');
    type(/contraseña/i, 'a long enough one');
    fireEvent.click(role(/mozo/i));
    save();

    expect(create).toHaveBeenCalledWith({
      username: 'Martin.P',
      password: 'a long enough one',
      role: 'Waiter',
    });
  });

  it('sends the role that was picked', async () => {
    const { rendered, create } = await openScreen();

    type(/usuario/i, 'martin.p');
    type(/contraseña/i, 'a long enough one');
    fireEvent.click(role(/KDS/i));
    await rendered.fixture.whenStable();
    save();

    expect(create).toHaveBeenCalledWith(expect.objectContaining({ role: 'Kds' }));
  });

  // Criterion 3 as the administrator sees it: a message about the field they
  // have to change, not a generic failure.
  it('says the username is already used in this venue', async () => {
    const { rendered } = await openScreen(rejectedWith(409, ProblemTypes.usernameTaken));

    type(/usuario/i, 'martin.p');
    type(/contraseña/i, 'a long enough one');
    fireEvent.click(role(/KDS/i));
    save();
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('Ya hay alguien');
  });

  // Retyping the whole form after a rejection is what makes people give up on a
  // screen. Only the password is worth clearing, and not even that: nobody else
  // is looking at an administrator's laptop.
  it('keeps what was typed when the API rejects it', async () => {
    const { rendered } = await openScreen(rejectedWith(409, ProblemTypes.usernameTaken));

    type(/usuario/i, 'martin.p');
    type(/contraseña/i, 'a long enough one');
    fireEvent.click(role(/KDS/i));
    save();
    await rendered.fixture.whenStable();

    expect(field(/usuario/i).value).toBe('martin.p');
  });

  /**
   * The administrator types a password they will read out loud to somebody
   * else. Dots are right by default, because this is a laptop on a bar, but
   * having to retype it to find out what they wrote is what makes them pick
   * something short instead.
   */
  it('hides the password behind dots until somebody asks to see it', async () => {
    await openScreen();

    expect(field(/contraseña/i).type).toBe('password');
  });

  it('shows the password while the eye is on', async () => {
    const { rendered } = await openScreen();

    eye().click();
    await rendered.fixture.whenStable();

    expect(field(/contraseña/i).type).toBe('text');
  });

  it('hides it again on the second press', async () => {
    const { rendered } = await openScreen();

    eye().click();
    await rendered.fixture.whenStable();
    eye().click();
    await rendered.fixture.whenStable();

    expect(field(/contraseña/i).type).toBe('password');
  });

  // The label has to say what pressing it will do, not what it did: a screen
  // reader user cannot see the dots to work out which way round it is.
  it('says what the eye will do next', async () => {
    const { rendered } = await openScreen();

    expect(eye().getAttribute('aria-label')).toBe('Mostrar la contraseña');

    eye().click();
    await rendered.fixture.whenStable();

    expect(eye().getAttribute('aria-label')).toBe('Ocultar la contraseña');
  });

  // A button inside a form submits it unless it says otherwise, and this one
  // would send a half-filled form every time somebody peeked.
  it('does not try to save when the eye is pressed', async () => {
    const { rendered, create } = await openScreen();

    type(/usuario/i, 'martin.p');
    type(/contraseña/i, 'a long enough one');
    fireEvent.click(role(/mozo/i));
    await rendered.fixture.whenStable();

    eye().click();
    await rendered.fixture.whenStable();

    expect(create).not.toHaveBeenCalled();
  });

  // What is on screen while it saves is what gets handed over, so the eye keeps
  // whatever state it had; what it must not do is stay usable on a dead form.
  it('locks the eye along with the rest of the form', async () => {
    const { rendered } = await openScreen(vi.fn().mockReturnValue(new Subject<StaffUser>()));

    type(/usuario/i, 'martin.p');
    type(/contraseña/i, 'a long enough one');
    fireEvent.click(role(/mozo/i));
    await rendered.fixture.whenStable();
    save();
    await rendered.fixture.whenStable();

    expect(eye().disabled).toBe(true);
  });

  it('locks the form while it is saving', async () => {
    const { rendered } = await openScreen(vi.fn().mockReturnValue(new Subject<StaffUser>()));

    type(/usuario/i, 'martin.p');
    type(/contraseña/i, 'a long enough one');
    fireEvent.click(role(/KDS/i));
    save();
    await rendered.fixture.whenStable();

    expect(field(/usuario/i).disabled).toBe(true);
  });
});
