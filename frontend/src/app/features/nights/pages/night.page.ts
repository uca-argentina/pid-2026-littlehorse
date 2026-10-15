import { httpResource } from '@angular/common/http';
import { Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProblemTypes } from '../../../core/api/problem-types';
import { problemTypeOf } from '../../../core/api/problem-type-of';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { STAFF_USERS_URL } from '../../staff-users/staff-users.service';
import type { StaffUser } from '../../staff-users/staff-users.service';
import { TABLES_URL } from '../../tables/tables.service';
import type { VipTable } from '../../tables/tables.service';
import { formatPrice } from '../../../shared/money/price';
import { NightForm } from '../components/night-form/night-form';
import { NightStock } from '../components/night-stock/night-stock';
import { nightHours, phaseOf } from '../night-calendar';
import type { NightPhase } from '../night-calendar';
import { NIGHTS_URL } from '../nights.service';
import type { NewNight, Night, NightTable } from '../nights.service';
import { SaveNightStore, refusalFor } from '../save-night.store';

const PHASE_NAMES: Record<NightPhase, string> = {
  upcoming: 'Próxima',
  underway: 'En curso',
  over: 'Terminada',
};

/** Why the night is not on screen. */
type LoadFailure = 'none' | 'notFound' | 'unreachable';

/**
 * A night's own screen (US-35), reached from the listing: editable while it
 * has not ended, read-only after. Its stock (US-37) goes under the form. The API holds the same rules; this only
 * avoids offering what it would refuse.
 */
@Component({
  selector: 'drinkit-night-page',
  imports: [AdminHeader, NightForm, NightStock, RouterLink],
  providers: [SaveNightStore],
  styleUrl: './night-pages.scss',
  templateUrl: './night.page.html',
})
export class NightPage {
  protected readonly store = inject(SaveNightStore);

  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  readonly id = input.required<string>();

  protected readonly night = httpResource<Night>(() => `${NIGHTS_URL}/${this.id()}`);

  protected readonly team = httpResource<StaffUser[]>(() => STAFF_USERS_URL);

  /** The venue's VIP tables, for the form to offer (US-40). */
  protected readonly venueTables = httpResource<VipTable[]>(() => TABLES_URL);

  /** The tables this night sells, with their budgets. */
  protected readonly nightTables = httpResource<NightTable[]>(
    () => `${NIGHTS_URL}/${this.id()}/tables`,
  );

  /**
   * Everything the form starts from. Drawn before the night's tables arrive,
   * a save would send none of them and take every table out of the night.
   */
  protected readonly ready = computed(
    () =>
      this.night.hasValue() &&
      this.team.hasValue() &&
      this.venueTables.hasValue() &&
      this.nightTables.hasValue(),
  );

  /** "Mesa 5 · $ 150.000,00", for a finished night that is only read. */
  protected readonly tableLines = computed(() =>
    (this.nightTables.hasValue() ? this.nightTables.value() : []).map(
      (table) => `Mesa ${table.number} · $ ${formatPrice(table.budget)}`,
    ),
  );

  /** Read against the moment it arrived, as the listing does. */
  protected readonly phase = computed<NightPhase | null>(() =>
    this.night.hasValue() ? phaseOf(this.night.value(), new Date()) : null,
  );

  protected readonly phaseName = computed(() => {
    const phase = this.phase();

    return phase === null ? '' : PHASE_NAMES[phase];
  });

  protected readonly hours = computed(() =>
    this.night.hasValue() ? nightHours(this.night.value()) : '',
  );

  /** A finished night's crew, by name, for the read-only view. */
  protected readonly crewNames = computed(() => {
    if (!this.night.hasValue()) return [];

    const team = this.team.hasValue() ? this.team.value() : [];

    return this.night
      .value()
      .crewIds.map((id) => team.find((user) => user.id === id)?.username ?? id);
  });

  protected readonly failure = computed<LoadFailure>(() => {
    const error =
      this.night.error() ??
      this.team.error() ??
      this.venueTables.error() ??
      this.nightTables.error();

    if (error === undefined) return 'none';

    return problemTypeOf(error) === ProblemTypes.nightNotFound ? 'notFound' : 'unreachable';
  });

  protected readonly refusal = computed(() => refusalFor(this.store.status()));

  protected retry(): void {
    if (this.night.error() !== undefined) this.night.reload();
    if (this.team.error() !== undefined) this.team.reload();
    if (this.venueTables.error() !== undefined) this.venueTables.reload();
    if (this.nightTables.error() !== undefined) this.nightTables.reload();
  }

  protected save(night: NewNight): void {
    this.store.update(this.venueSlug(), this.id(), night);
  }
}
