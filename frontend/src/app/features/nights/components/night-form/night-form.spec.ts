import { fireEvent, render, screen } from '@testing-library/angular';
import type { StaffUser } from '../../../staff-users/staff-users.service';
import type { VipTable } from '../../../tables/tables.service';
import type { Night, NightTable } from '../../nights.service';
import { NightForm } from './night-form';

const noAudit = { createdAt: null, createdBy: null, lastModifiedAt: null, lastModifiedBy: null };

function account(id: string, role: StaffUser['role'], isActive = true): StaffUser {
  return { id, username: id, displayName: null, role, isActive, audit: noAudit };
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

function vipTable(id: string, number: number, extra: Partial<VipTable> = {}): VipTable {
  return { id, number, capacity: 8, location: null, isActive: true, audit: noAudit, ...extra };
}

const theTables: VipTable[] = [
  vipTable('t5', 5, { capacity: 10, location: 'Al lado de la cabina' }),
  vipTable('t7', 7),
  vipTable('t9', 9, { isActive: false }),
];

async function openForm(
  inputs: Partial<{
    initial: Night;
    startLocked: boolean;
    tables: VipTable[];
    initialTables: NightTable[];
  }> = {},
) {
  const saved = vi.fn();
  const rendered = await render(NightForm, {
    inputs: { team: theTeam, tables: theTables, submitLabel: 'Guardar', ...inputs },
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
      .filter((box) => !box.closest('fieldset'))
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
      tables: [],
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
      tables: [],
    });
  });

  // A night underway keeps its start: its orders already belong to it.
  it('locks the start of a night that already began, and says why', async () => {
    await openForm({ initial: saturday, startLocked: true });

    expect((screen.getByLabelText(/empieza/i) as HTMLInputElement).disabled).toBe(true);
    expect(screen.getByText(/ya empezó/i)).not.toBeNull();
  });

  describe('VIP tables', () => {
    function tableBox(number: number): HTMLInputElement {
      return screen.getByRole('checkbox', {
        name: new RegExp(`^Mesa ${number}\\b`),
      }) as HTMLInputElement;
    }

    function budgetOf(number: number): HTMLInputElement {
      return screen.getByLabelText(`Presupuesto de la mesa ${number}`) as HTMLInputElement;
    }

    async function aValidCrew(rendered: Awaited<ReturnType<typeof openForm>>['rendered']) {
      fillTheNight();
      await openThePickers(rendered);
      pick('main-bar');
      pick('till-1');
      pick('martin');
    }

    // US-39: a table put away no longer shows up to be given a night.
    it('offers the active tables of the venue, with their people and location', async () => {
      await openForm();

      expect(tableBox(5).getAttribute('aria-describedby')).not.toBeNull();
      expect(tableBox(5).closest('li')?.textContent).toContain(
        '10 personas · Al lado de la cabina',
      );
      expect(tableBox(7)).not.toBeNull();
      expect(screen.queryByRole('checkbox', { name: /^Mesa 9\b/ })).toBeNull();
    });

    it('hands over the chosen tables with their budgets, in pesos', async () => {
      const { rendered, saved } = await openForm();

      await aValidCrew(rendered);
      tableBox(5).click();
      tableBox(7).click();
      await rendered.fixture.whenStable();
      fireEvent.input(budgetOf(5), { target: { value: '150.000' } });
      fireEvent.input(budgetOf(7), { target: { value: '90000' } });
      await save(rendered);

      expect(saved).toHaveBeenCalledWith(
        expect.objectContaining({
          tables: [
            { tableId: 't5', budget: 150000 },
            { tableId: 't7', budget: 90000 },
          ],
        }),
      );
    });

    it('does not hand over a chosen table without its budget', async () => {
      const { rendered, saved } = await openForm();

      await aValidCrew(rendered);
      tableBox(5).click();
      await save(rendered);

      expect(saved).not.toHaveBeenCalled();
      expect(budgetOf(5).getAttribute('aria-invalid')).toBe('true');
    });

    // §8.3: somebody has to carry the drinks to the tables.
    it('asks for a waiter when the night sells tables', async () => {
      const { rendered, saved } = await openForm();

      fillTheNight();
      await openThePickers(rendered);
      pick('main-bar');
      pick('till-1');
      tableBox(5).click();
      await rendered.fixture.whenStable();
      fireEvent.input(budgetOf(5), { target: { value: '1000' } });
      await save(rendered);

      expect(saved).not.toHaveBeenCalled();
      expect(screen.getByText(/con mesas vip, la noche necesita al menos un mozo/i)).not.toBeNull();
    });

    it('adds every table at once, and takes them all out again', async () => {
      const { rendered } = await openForm();

      screen.getByRole('button', { name: /agregar todas/i }).click();
      await rendered.fixture.whenStable();

      expect([tableBox(5).checked, tableBox(7).checked]).toEqual([true, true]);

      screen.getByRole('button', { name: /sacar todas/i }).click();
      await rendered.fixture.whenStable();

      expect([tableBox(5).checked, tableBox(7).checked]).toEqual([false, false]);
    });

    it('starts from the tables the night already sells', async () => {
      await openForm({
        initial: { ...saturday, crewIds: ['main-bar', 'till-1', 'martin'] },
        initialTables: [
          {
            tableId: 't7',
            number: 7,
            capacity: 8,
            location: null,
            budget: 90000,
            spent: 0,
            accessCode: 'a'.repeat(32),
          },
        ],
      });

      expect(tableBox(5).checked).toBe(false);
      expect(tableBox(7).checked).toBe(true);
      expect(budgetOf(7).value).toBe('90000');
    });

    it('leaves out a table that is unticked', async () => {
      const { rendered, saved } = await openForm({
        initial: { ...saturday, crewIds: ['main-bar', 'till-1', 'martin'] },
        initialTables: [
          {
            tableId: 't7',
            number: 7,
            capacity: 8,
            location: null,
            budget: 90000,
            spent: 0,
            accessCode: 'a'.repeat(32),
          },
        ],
      });

      tableBox(7).click();
      await save(rendered);

      expect(saved).toHaveBeenCalledWith(expect.objectContaining({ tables: [] }));
    });
  });
});
