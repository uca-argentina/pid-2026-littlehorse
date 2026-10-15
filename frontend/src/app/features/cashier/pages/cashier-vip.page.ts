import { httpResource } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { problemTypeOf } from '../../../core/api/problem-type-of';
import { ProblemTypes } from '../../../core/api/problem-types';
import { StaffHeader } from '../../../shared/staff-header/staff-header';
import { elapsedSince } from '../../../shared/time/elapsed-since';
import { CASHIER_SECTIONS } from '../cashier-sections';
import { CASHIER_VIP_DELIVERIES_URL } from '../cashier.service';
import type { VipDelivery } from '../cashier.service';
import { TillChannel } from '../till-channel';

/** How often the "hace cuánto" figures are recomputed, in milliseconds. */
const CLOCK_MS = 15_000;

/**
 * "Entregas VIP" (US-45), from design/wireframes/CajeroVip: the tables that
 * are ready and nobody has taken, so the cashier can shout to a waiter before
 * the drink goes warm, and apart the ones a waiter already carries.
 *
 * It only looks. Nobody assigns tables (§8.3): coordinating is done out loud.
 * The list moves on its own through the hub the till already listens to.
 */
@Component({
  selector: 'drinkit-cashier-vip-page',
  imports: [StaffHeader],
  styleUrl: './cashier-vip.page.scss',
  templateUrl: './cashier-vip.page.html',
})
export class CashierVipPage {
  private readonly destroyRef = inject(DestroyRef);

  /** Hears when something changed, so the list reloads on its own. */
  protected readonly channel = inject(TillChannel);

  readonly venueSlug = input.required<string>();

  protected readonly sections = CASHIER_SECTIONS;

  protected readonly deliveries = httpResource<VipDelivery[]>(() => CASHIER_VIP_DELIVERIES_URL);

  /** US-35, criterion 4: signed in as a cashier, but not in tonight's crew. */
  protected readonly notTonight = computed(
    () => problemTypeOf(this.deliveries.error()) === ProblemTypes.notInTonightsCrew,
  );

  protected readonly unreachable = computed(
    () => this.deliveries.error() !== undefined && !this.notTonight(),
  );

  // hasValue() and not value(): reading the value of a failed resource throws,
  // and the template reads this on every change detection, error state included.
  private readonly everything = computed<VipDelivery[]>(() =>
    this.deliveries.hasValue() ? this.deliveries.value() : [],
  );

  /** Nobody took them: the one that has waited longest comes first, as the API sends them. */
  protected readonly waiting = computed(() =>
    this.everything().filter((table) => table.takenByName === null),
  );

  protected readonly taken = computed(() =>
    this.everything().filter((table) => table.takenByName !== null),
  );

  protected readonly isEmpty = computed(
    () => this.deliveries.hasValue() && this.everything().length === 0,
  );

  /** What "hace cuánto" is measured against. A signal, so the figures keep moving. */
  private readonly now = signal(Date.now());

  constructor() {
    this.channel.connect(() => this.deliveries.reload());

    const clock = setInterval(() => this.now.set(Date.now()), CLOCK_MS);

    this.destroyRef.onDestroy(() => {
      clearInterval(clock);
      this.channel.disconnect();
    });
  }

  protected since(moment: string | null): string {
    return elapsedSince(this.now(), moment);
  }

  /** "01:05": the moment it became ready, in the venue's own clock. */
  protected clock(moment: string | null): string {
    if (moment === null) return '';

    return new Date(moment).toLocaleTimeString('es-AR', { hour: '2-digit', minute: '2-digit' });
  }
}
