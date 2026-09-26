import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { of, throwError } from 'rxjs';
import { ProblemTypes } from '../../../core/api/problem-types';
import { EditStaffUserStore } from '../edit-staff-user.store';
import { STAFF_USERS_URL, StaffUsersService } from '../staff-users.service';
import type { StaffUser } from '../staff-users.service';
import { EditStaffUserPage } from './edit-staff-user.page';

const noAudit = { createdAt: null, createdBy: null, lastModifiedAt: null, lastModifiedBy: null };

const martin: StaffUser = {
  id: 'id-2',
  username: 'martin.p',
  role: 'Waiter',
  isActive: true,
  audit: noAudit,
};

const theTeam: StaffUser[] = [
  { id: 'id-1', username: 'euge.q', role: 'Administrator', isActive: true, audit: noAudit },
  martin,
  {
    id: 'id-3',
    username: 'pablo.l',
    role: 'Kds',
    isActive: false,
    audit: {
      createdAt: '2026-09-27T21:00:00Z',
      createdBy: 'euge.q',
      lastModifiedAt: '2026-09-28T01:30:00Z',
      lastModifiedBy: 'nico.r',
    },
  },
];

const rejectedWith = (status: number, type: string) =>
  vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status, error: { type } })));

async function openScreenFor(id: string, overrides: Record<string, unknown> = {}) {
  const staffUsers = {
    changeRole: vi.fn().mockReturnValue(of(martin)),
    resetPassword: vi.fn().mockReturnValue(of(martin)),
    deactivate: vi.fn().mockReturnValue(of({ ...martin, isActive: false })),
    reactivate: vi.fn().mockReturnValue(of({ ...martin, isActive: true })),
    ...overrides,
  };

  const rendered = await render(EditStaffUserPage, {
    inputs: { venueSlug: 'bar-alfa', id },
    providers: [
      provideRouter([{ path: ':venueSlug/staff/users', children: [] }]),
      provideHttpClient(),
      provideHttpClientTesting(),
      EditStaffUserStore,
      { provide: StaffUsersService, useValue: staffUsers },
    ],
  });

  TestBed.inject(HttpTestingController).expectOne(STAFF_USERS_URL).flush(theTeam);
  await rendered.fixture.whenStable();

  return { rendered, staffUsers };
}

function role(name: RegExp): HTMLInputElement {
  return screen.getByRole('radio', { name }) as HTMLInputElement;
}

function press(name: RegExp): void {
  screen.getByRole('button', { name }).click();
}

/**
 * The whole two-step journey, for the tests that are about what follows it.
 * The screen has to be let redraw in between: the button to confirm with does
 * not exist until the first touch has been rendered.
 */
async function takeAccessAway(rendered: { fixture: { whenStable: () => Promise<unknown> } }) {
  press(/dar de baja/i);
  await rendered.fixture.whenStable();

  press(/confirmar la baja/i);
}

describe('EditStaffUserPage', () => {
  // US-30, criteria 1 and 2, on the ficha of a person.
  it('says who added this person and who last changed them', async () => {
    await openScreenFor('id-3');

    expect(screen.getByText(/creado por euge\.q/i)).not.toBeNull();
    expect(screen.getByText(/última modificación por nico\.r/i)).not.toBeNull();
  });

  // Criterion 4: nobody is given a date they never had.
  it('says there is no record for somebody who was there before the audit existed', async () => {
    await openScreenFor('id-2');

    expect(screen.getByText(/sin registro de quién lo creó ni de cuándo/i)).not.toBeNull();
  });

  it('names whoever is being corrected', async () => {
    await openScreenFor('id-2');

    expect(screen.getByRole('heading', { level: 1 }).textContent).toContain('martin.p');
  });

  // US-04, criterion 2: the role they have now is the one already selected, so
  // an administrator sees what they are changing from.
  it('starts on the role that person already has', async () => {
    await openScreenFor('id-2');

    expect(role(/mozo/i).checked).toBe(true);
    expect(role(/administrador/i).checked).toBe(false);
  });

  it('sends the role that was picked', async () => {
    const { rendered, staffUsers } = await openScreenFor('id-2');

    fireEvent.click(role(/KDS/i));
    await rendered.fixture.whenStable();
    press(/guardar el rol/i);

    expect(staffUsers['changeRole']).toHaveBeenCalledWith('id-2', 'Kds');
  });

  it('confirms the role was saved', async () => {
    const { rendered } = await openScreenFor('id-2');

    fireEvent.click(role(/KDS/i));
    await rendered.fixture.whenStable();
    press(/guardar el rol/i);
    await rendered.fixture.whenStable();

    expect(screen.getByText(/rol guardado/i)).not.toBeNull();
  });

  // US-04, criterion 3.
  it('sends the new password', async () => {
    const { rendered, staffUsers } = await openScreenFor('id-2');

    fireEvent.input(screen.getByLabelText(/contraseña/i, { selector: 'input' }), {
      target: { value: 'a long enough one' },
    });
    await rendered.fixture.whenStable();
    press(/cambiar la contraseña/i);

    expect(staffUsers['resetPassword']).toHaveBeenCalledWith('id-2', 'a long enough one');
  });

  // The form stops it, so the venue's connection is never part of finding out
  // that eight characters means eight.
  it('does not send a password under eight characters', async () => {
    const { rendered, staffUsers } = await openScreenFor('id-2');

    fireEvent.input(screen.getByLabelText(/contraseña/i, { selector: 'input' }), {
      target: { value: 'corta' },
    });
    await rendered.fixture.whenStable();
    press(/cambiar la contraseña/i);
    await rendered.fixture.whenStable();

    expect(staffUsers['resetPassword']).not.toHaveBeenCalled();
    expect(screen.getByText(/al menos ocho caracteres/i)).not.toBeNull();
  });

  /**
   * Taking somebody's access away asks first.
   *
   * This screen is used standing up, fast, on a tablet that is passed around,
   * and "Dar de baja" sits in the same vertical run as "Guardar el rol" and
   * "Cambiar la contraseña". One stray touch logs a colleague out in the middle
   * of their shift, and nothing on this screen undoes it in one step.
   */
  it('does not take access away on the first touch', async () => {
    const { rendered, staffUsers } = await openScreenFor('id-2');

    press(/dar de baja/i);
    await rendered.fixture.whenStable();

    expect(staffUsers['deactivate']).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: /confirmar la baja/i })).not.toBeNull();
  });

  it('lets whoever asked to deactivate back out of it', async () => {
    const { rendered, staffUsers } = await openScreenFor('id-2');

    press(/dar de baja/i);
    await rendered.fixture.whenStable();

    press(/mejor no/i);
    await rendered.fixture.whenStable();

    expect(staffUsers['deactivate']).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: /dar de baja/i })).not.toBeNull();
  });

  // A double tap lands its second touch where the first one was: that spot
  // has to back out, not confirm.
  it('puts the way back first, where the first tap landed', async () => {
    const { rendered } = await openScreenFor('id-2');

    press(/dar de baja/i);
    await rendered.fixture.whenStable();

    const [first] = screen
      .getAllByRole('button')
      .filter((button) => /dar de baja|mejor no|confirmar la baja/i.test(button.textContent ?? ''));

    expect(first.textContent).toMatch(/mejor no/i);
  });

  // US-05, criterion 1.
  it('takes access away without offering to delete anything', async () => {
    const { rendered, staffUsers } = await openScreenFor('id-2');

    await takeAccessAway(rendered);

    expect(staffUsers['deactivate']).toHaveBeenCalledWith('id-2');
    expect(screen.queryByRole('button', { name: /borrar|eliminar/i })).toBeNull();
  });

  // US-05, criterion 4: somebody who was deactivated is offered the way back,
  // not the way out again.
  it('offers to reactivate whoever was deactivated', async () => {
    const { staffUsers } = await openScreenFor('id-3');

    expect(screen.queryByRole('button', { name: /dar de baja/i })).toBeNull();

    press(/reactivar/i);

    expect(staffUsers['reactivate']).toHaveBeenCalledWith('id-3');
  });

  it('shows the new state after taking access away', async () => {
    const { rendered } = await openScreenFor('id-2');

    await takeAccessAway(rendered);
    await rendered.fixture.whenStable();

    expect(screen.getByRole('button', { name: /reactivar/i })).not.toBeNull();
  });

  // The one refusal an administrator can act on: hand the role over first.
  it('says when the venue would be left without an administrator', async () => {
    const { rendered } = await openScreenFor('id-1', {
      deactivate: rejectedWith(409, ProblemTypes.lastAdministrator),
    });

    await takeAccessAway(rendered);
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('último administrador');
  });

  it('says so when somebody with that id is not in this venue', async () => {
    const rendered = await render(EditStaffUserPage, {
      inputs: { venueSlug: 'bar-alfa', id: 'nobody' },
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        EditStaffUserStore,
        { provide: StaffUsersService, useValue: {} },
      ],
    });

    TestBed.inject(HttpTestingController).expectOne(STAFF_USERS_URL).flush(theTeam);
    await rendered.fixture.whenStable();

    expect(screen.getByRole('status').textContent).toContain('No encontramos');
  });

  it('offers the way back to the listing', async () => {
    await openScreenFor('id-2');

    expect(screen.getByRole('link', { name: /volver/i }).getAttribute('href')).toBe(
      '/bar-alfa/staff/users',
    );
  });
});
