import { httpResource } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { StaffRole } from '../../../core/staff/staff-roles';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { STAFF_USERS_URL } from '../../staff-users/staff-users.service';
import type { StaffUser } from '../../staff-users/staff-users.service';
import { NewNightStore } from '../new-night.store';

/** One block of checkboxes: the accounts of one role. */
interface CrewGroup {
  readonly role: StaffRole;
  readonly title: string;
  readonly whenNone: string;
  readonly accounts: readonly { id: string; username: string }[];
}

/**
 * The roles that work a night, in the order the form asks for them. No
 * administrator: the night never limits them (US-35).
 */
const CREW_ROLES: readonly Omit<CrewGroup, 'accounts'>[] = [
  { role: 'Kds', title: 'KDS', whenNone: 'No hay cuentas de KDS activas.' },
  { role: 'Cashier', title: 'Cajeros', whenNone: 'No hay cajeros activos.' },
  { role: 'Waiter', title: 'Mozos', whenNone: 'No hay mozos activos. Son opcionales.' },
];

@Component({
  selector: 'drinkit-new-night-page',
  imports: [AdminHeader, RouterLink],
  // On the component, as in NewStaffUserPage: a stale failure dies with it.
  providers: [NewNightStore],
  styleUrl: './new-night.page.scss',
  templateUrl: './new-night.page.html',
})
export class NewNightPage {
  protected readonly store = inject(NewNightStore);

  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  /** The whole team; the form keeps the accounts that can work a night. */
  protected readonly team = httpResource<StaffUser[]>(() => STAFF_USERS_URL);

  protected readonly name = signal('');

  /** As a datetime-local input writes it: "2026-10-10T23:00", in local time. */
  protected readonly startsAt = signal('');

  protected readonly endsAt = signal('');

  protected readonly crew = signal<ReadonlySet<string>>(new Set());

  /** Errors stay hidden until the first attempt: flagging while typing is noise. */
  private readonly attempted = signal(false);

  protected readonly groups = computed<CrewGroup[]>(() => {
    const active = (this.team.hasValue() ? this.team.value() : []).filter((user) => user.isActive);

    return CREW_ROLES.map((group) => ({
      ...group,
      accounts: active
        .filter((user) => user.role === group.role)
        .map((user) => ({ id: user.id, username: user.username })),
    }));
  });

  private readonly chosenRoles = computed(() => {
    const chosen = this.crew();

    return new Set(
      this.groups()
        .filter((group) => group.accounts.some((account) => chosen.has(account.id)))
        .map((group) => group.role),
    );
  });

  protected readonly nameError = computed(() =>
    this.shown(this.name().trim() === '', 'Ponele un nombre a la noche.'),
  );

  protected readonly hoursError = computed(() => {
    const start = Date.parse(this.startsAt());
    const end = Date.parse(this.endsAt());

    if (Number.isNaN(start) || Number.isNaN(end))
      return this.shown(true, 'Completá cuándo empieza y cuándo termina.');

    return this.shown(
      end <= start,
      'La noche tiene que terminar después de empezar. Si cruza la medianoche, el fin es al día siguiente.',
    );
  });

  protected readonly kdsError = computed(() =>
    this.shown(!this.chosenRoles().has('Kds'), 'Elegí al menos una KDS.'),
  );

  protected readonly cashierError = computed(() =>
    this.shown(!this.chosenRoles().has('Cashier'), 'Elegí al menos un cajero.'),
  );

  /** Said next to the button, since the field that is wrong may be far above it. */
  protected readonly formError = computed(() => {
    const errors = [this.nameError(), this.hoursError(), this.kdsError(), this.cashierError()];

    return errors.some((error) => error !== null) ? 'Revisá lo marcado más arriba.' : null;
  });

  protected readonly teamFailed = computed(() => this.team.error() !== undefined);

  protected typed(field: 'name' | 'startsAt' | 'endsAt', event: Event): void {
    this[field].set((event.target as HTMLInputElement).value);
  }

  protected toggle(id: string): void {
    this.crew.update((chosen) => {
      const next = new Set(chosen);

      if (next.has(id)) next.delete(id);
      else next.add(id);

      return next;
    });
  }

  protected retry(): void {
    this.team.reload();
  }

  protected submit(): void {
    this.attempted.set(true);

    if (this.formError() !== null || this.store.isSending()) return;

    // In the order the form shows them, not the order they were ticked.
    const crewIds = this.groups()
      .flatMap((group) => group.accounts)
      .filter((account) => this.crew().has(account.id))
      .map((account) => account.id);

    this.store.submit(this.venueSlug(), {
      name: this.name().trim(),
      // The input speaks local time; the API takes an instant.
      startsAt: new Date(this.startsAt()).toISOString(),
      endsAt: new Date(this.endsAt()).toISOString(),
      crewIds,
    });
  }

  private shown(broken: boolean, message: string): string | null {
    return this.attempted() && broken ? message : null;
  }
}
