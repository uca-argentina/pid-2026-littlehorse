import { Component, computed, input, linkedSignal, output, signal } from '@angular/core';
import type { StaffRole } from '../../../../core/staff/staff-roles';
import type { StaffUser } from '../../../staff-users/staff-users.service';
import type { NewNight, Night } from '../../nights.service';
import { CrewPicker } from '../crew-picker/crew-picker';
import type { CrewAccount } from '../crew-picker/crew-picker';

/** The accounts of one role, as one dropdown of the form. */
interface CrewGroup {
  readonly role: StaffRole;
  readonly title: string;
  readonly whenNone: string;
  readonly accounts: readonly CrewAccount[];
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

/** "2026-10-10T23:00", in local time, as a datetime-local input reads and writes it. */
function toLocalInput(instant: string | undefined): string {
  if (instant === undefined) return '';

  const date = new Date(instant);
  const pad = (value: number) => String(value).padStart(2, '0');

  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/**
 * The night's fields and their rules, shared by "Nueva noche" and the night's
 * own screen so the two can never validate differently. It hands over what was
 * filled in; saving it, and what the API answers, is the page's business.
 */
@Component({
  selector: 'drinkit-night-form',
  imports: [CrewPicker],
  styleUrl: './night-form.scss',
  templateUrl: './night-form.html',
})
export class NightForm {
  /** The whole team; the form keeps the accounts that can work a night. */
  readonly team = input.required<readonly StaffUser[]>();

  /** The saved night, on its own screen. Nothing on "Nueva noche". */
  readonly initial = input<Night | null>(null);

  /** A night underway keeps its start: its orders already belong to it. */
  readonly startLocked = input(false);

  readonly sending = input(false);

  /** What the API refused, said next to the button. */
  readonly refusal = input<string | null>(null);

  readonly submitLabel = input.required<string>();

  readonly saved = output<NewNight>();

  protected readonly name = linkedSignal(() => this.initial()?.name ?? '');

  protected readonly startsAt = linkedSignal(() => toLocalInput(this.initial()?.startsAt));

  protected readonly endsAt = linkedSignal(() => toLocalInput(this.initial()?.endsAt));

  protected readonly crew = linkedSignal<ReadonlySet<string>>(
    () => new Set(this.initial()?.crewIds ?? []),
  );

  /** Errors stay hidden until the first attempt: flagging while typing is noise. */
  private readonly attempted = signal(false);

  protected readonly groups = computed<CrewGroup[]>(() => {
    const active = this.team().filter((user) => user.isActive);

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

  protected errorFor(role: StaffRole): string | null {
    if (role === 'Kds') return this.kdsError();
    if (role === 'Cashier') return this.cashierError();

    return null;
  }

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

  protected submit(): void {
    this.attempted.set(true);

    if (this.formError() !== null || this.sending()) return;

    // In the order the form shows them, not the order they were ticked.
    const crewIds = this.groups()
      .flatMap((group) => group.accounts)
      .filter((account) => this.crew().has(account.id))
      .map((account) => account.id);

    this.saved.emit({
      name: this.name().trim(),
      startsAt: this.instantOf(this.startsAt(), this.initial()?.startsAt),
      endsAt: this.instantOf(this.endsAt(), this.initial()?.endsAt),
      crewIds,
    });
  }

  /**
   * The input speaks local time to the minute; the API takes an instant. An
   * untouched field hands back the saved instant as it was, seconds included:
   * the server compares the start of a night underway exactly.
   */
  private instantOf(typed: string, saved: string | undefined): string {
    if (saved !== undefined && typed === toLocalInput(saved)) return saved;

    return new Date(typed).toISOString();
  }

  private shown(broken: boolean, message: string): string | null {
    return this.attempted() && broken ? message : null;
  }
}
