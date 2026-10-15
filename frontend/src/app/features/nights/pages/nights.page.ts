import { httpResource } from '@angular/common/http';
import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProblemTypes } from '../../../core/api/problem-types';
import { problemTypeOf } from '../../../core/api/problem-type-of';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { slowLoading } from '../../../shared/loading/slow-loading';
import { nightHours, phaseOf } from '../night-calendar';
import type { NightPhase } from '../night-calendar';
import { NIGHTS_URL } from '../nights.service';
import type { Night } from '../nights.service';

/** One row of the listing, already in the venue's language. */
interface NightRow {
  readonly id: string;
  readonly name: string;
  readonly hours: string;
  readonly phase: NightPhase;
  readonly phaseName: string;
  readonly crew: string;
}

const PHASE_NAMES: Record<NightPhase, string> = {
  upcoming: 'Próxima',
  underway: 'En curso',
  over: 'Terminada',
};

/** Why the listing is not on screen. */
type ListingFailure = 'none' | 'forbidden' | 'unreachable';

@Component({
  selector: 'drinkit-nights-page',
  imports: [AdminHeader, RouterLink],
  styleUrl: './nights.page.scss',
  templateUrl: './nights.page.html',
})
export class NightsPage {
  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  protected readonly nights = httpResource<Night[]>(() => NIGHTS_URL);

  protected readonly isSlow = slowLoading(() => this.nights.isLoading());

  protected readonly skeletonRows = [1, 2, 3];

  /**
   * The phase is read against the moment the list arrived, not kept ticking: a
   * night that starts while the screen is open shows up on the next visit,
   * which is soon enough for a screen nobody stares at.
   */
  protected readonly rows = computed<NightRow[]>(() => {
    if (!this.nights.hasValue()) return [];

    const now = new Date();

    return this.nights.value().map((night) => {
      const phase = phaseOf(night, now);

      return {
        id: night.id,
        name: night.name,
        hours: nightHours(night),
        phase,
        phaseName: PHASE_NAMES[phase],
        crew: night.crewIds.length === 1 ? '1 persona' : `${night.crewIds.length} personas`,
      };
    });
  });

  protected readonly failure = computed<ListingFailure>(() => {
    const error = this.nights.error();

    if (error === undefined) return 'none';

    return problemTypeOf(error) === ProblemTypes.forbidden ? 'forbidden' : 'unreachable';
  });

  protected readonly isEmpty = computed(() => this.nights.hasValue() && this.rows().length === 0);

  protected retry(): void {
    this.nights.reload();
  }
}
