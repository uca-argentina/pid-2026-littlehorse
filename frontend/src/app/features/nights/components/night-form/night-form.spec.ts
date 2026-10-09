import { fireEvent, render, screen } from '@testing-library/angular';
import type { StaffUser } from '../../../staff-users/staff-users.service';
import type { Night } from '../../nights.service';
import { NightForm } from './night-form';

const noAudit = { createdAt: null, createdBy: null, lastModifiedAt: null, lastModifiedBy: null };

function account(id: string, role: StaffUser['role'], isActive = true): StaffUser {
  return { id, username: id, role, isActive, audit: noAudit };
}

const theTeam: StaffUser[] = [
  account('euge.q', 'Administrator'),
  account('main-bar', 'Kds'),
  account('vip-bar', 'Kds'),
  account('till-1', 'Cashier'),
  account('martin', 'Waiter'),
  account('former-waiter', 'Waiter', false),
];

const saturday: Night = {
  id: 'night-1',
  name: 'Saturday',
  startsAt: new Date(2026, 9, 10, 23, 0).toISOString(),
  endsAt: new Date(2026, 9, 11, 6, 0).toISOString(),
  crewIds: ['main-bar', 'till-1'],
  audit: noAudit,
};

async function openForm(inputs: Partial<{ initial: Night; startLocked: boolean }> = {}) {
  const saved = vi.fn();
  const rendered = await render(NightForm, {
    inputs: { team: theTeam, submitLabel: 'Guardar', ...inputs },
    on: { saved },
  });

  return { rendered, saved };
}

function type(label: RegExp, value: string): void {
  fireEvent.input(screen.getByLabelText(label), { target: { value } });
}

/** Opens every role's dropdown, as somebody filling the form would. */
async function openThePickers(rendered: Awaited<ReturnType<typeof openForm>>['rendered']) {
  for (const role of [/^KDS/, /^Cajeros/, /^Mozos/])
    screen.getByRole('button', { name: role }).click();
  await rendered.fixture.whenStable();
}

function pick(username: string): void {
  screen.getByRole('checkbox', { name: username }).click();
}

function fillTheNight(): void {
  type(/nombre/i, 'Saturday');
  type(/empieza/i, '2026-10-10T23:00');
  type(/termina/i, '2026-10-11T06:00');
}

async function save(rendered: Awaited<ReturnType<typeof openForm>>['rendered']): Promise<void> {
  screen.getByRole('button', { name: 'Guardar' }).click();
  await rendered.fixture.whenStable();
}

describe('NightForm', () => {
  // The crew is the venue's KDS, cashiers and waiters. The administrator is
  // never limited by the night, and a deactivated account cannot work it.
  it('offers the active KDS, cashiers and waiters, and nobody else', async () => {
    const { rendered } = await openForm();

    await openThePickers(rendered);

    const offered = screen
      .getAllByRole('checkbox')
      .map((box) => box.closest('label')?.textContent?.trim());
    expect(offered).toEqual(['main-bar', 'vip-bar', 'till-1', 'martin']);
  });

  it('hands over the night with its hours as instants and the chosen crew', async () => {
    const { rendered, saved } = await openForm();

    fillTheNight();
    await openThePickers(rendered);
    pick('main-bar');
    pick('till-1');
    pick('martin');
    await save(rendered);

    expect(saved).toHaveBeenCalledWith({
      name: 'Saturday',
      startsAt: new Date(2026, 9, 10, 23, 0).toISOString(),
      endsAt: new Date(2026, 9, 11, 6, 0).toISOString(),
      crewIds: ['main-bar', 'till-1', 'martin'],
    });
  });

  it('asks for a KDS, and says so next to the button too', async () => {
    const { rendered, saved } = await openForm();

    fillTheNight();
    await openThePickers(rendered);
    pick('till-1');
    await save(rendered);

    expect(screen.getByText('Elegí al menos una KDS.')).not.toBeNull();
    // Also next to the button: the KDS dropdown may be a long scroll above it.
    expect(screen.getByRole('alert').textContent).toContain('Revisá lo marcado más arriba');
    expect(saved).not.toHaveBeenCalled();
  });

  it('asks for a cashier before handing anything over', async () => {
    const { rendered, saved } = await openForm();

    fillTheNight();
    await openThePickers(rendered);
    pick('main-bar');
    await save(rendered);

    expect(screen.getByText('Elegí al menos un cajero.')).not.toBeNull();
    expect(saved).not.toHaveBeenCalled();
  });

  // A night crosses midnight, so an end "before" the start is the usual typo:
  // the right day was not picked for it.
  it('refuses an end that is not after the start', async () => {
    const { rendered, saved } = await openForm();

    fillTheNight();
    type(/termina/i, '2026-10-10T06:00');
    await openThePickers(rendered);
    pick('main-bar');
    pick('till-1');
    await save(rendered);

    expect(screen.getByText(/tiene que terminar después de empezar/i)).not.toBeNull();
    expect(saved).not.toHaveBeenCalled();
  });

  // The night's own screen opens on what was saved.
  it('starts from the night it is given', async () => {
    await openForm({ initial: saturday });

    expect((screen.getByLabelText(/nombre/i) as HTMLInputElement).value).toBe('Saturday');
    expect((screen.getByLabelText(/empieza/i) as HTMLInputElement).value).toBe('2026-10-10T23:00');
    expect((screen.getByLabelText(/termina/i) as HTMLInputElement).value).toBe('2026-10-11T06:00');
    expect(screen.getByRole('button', { name: /^KDS/ }).textContent).toContain('main-bar');
  });

  it('saves a night it was given without touching anything', async () => {
    const { rendered, saved } = await openForm({ initial: saturday });

    await save(rendered);

    expect(saved).toHaveBeenCalledWith({
      name: 'Saturday',
      startsAt: saturday.startsAt,
      endsAt: saturday.endsAt,
      crewIds: ['main-bar', 'till-1'],
    });
  });

  // A night underway keeps its start: its orders already belong to it.
  it('locks the start of a night that already began, and says why', async () => {
    await openForm({ initial: saturday, startLocked: true });

    expect((screen.getByLabelText(/empieza/i) as HTMLInputElement).disabled).toBe(true);
    expect(screen.getByText(/ya empezó/i)).not.toBeNull();
  });
});
