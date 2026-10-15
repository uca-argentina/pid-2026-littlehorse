import { httpResource } from '@angular/common/http';
import { Component, computed, effect, inject, input } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ProblemTypes } from '../../../core/api/problem-types';
import { problemTypeOf } from '../../../core/api/problem-type-of';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { formatPrice } from '../../../shared/money/price';
import { nightHours, phaseOf } from '../../nights/night-calendar';
import { NIGHTS_URL } from '../../nights/nights.service';
import type { Night } from '../../nights/nights.service';
import { formatMinutes, nightToOpen, reportableNights } from '../report-calendar';
import { metricsUrl } from '../reports.service';
import type { NightMetrics, PaymentMethodName } from '../reports.service';

/**
 * How often a night still underway asks for its numbers again. Nobody stares
 * at this screen the way the bar stares at its board, so half a minute is
 * live enough and costs the API next to nothing.
 */
export const REPORT_REFRESH_MS = 30_000;

/** The class each method is drawn with, in the stylesheet's own case. */
const METHOD_TONES: Record<PaymentMethodName, string> = {
  Cash: 'cash',
  Digital: 'digital',
  VipBalance: 'vip',
};

const METHOD_NAMES: Record<PaymentMethodName, string> = {
  Cash: 'Efectivo',
  Digital: 'Digital',
  VipBalance: 'Saldo VIP',
};

/** Why the report is not on screen. */
type LoadFailure = 'none' | 'notFound' | 'unreachable';

/**
 * A night's report (US-47 to US-49): what it sold and how, how long orders
 * took, and what was ordered most. Opened from its own tab, on the night that
 * matters, or from a night's screen.
 */
@Component({
  selector: 'drinkit-reports-page',
  imports: [AdminHeader, RouterLink],
  styleUrl: './reports.page.scss',
  templateUrl: './reports.page.html',
})
export class ReportsPage {
  private readonly router = inject(Router);

  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  /** From the path too. None when the tab was opened on its own. */
  readonly nightId = input<string>();

  protected readonly nights = httpResource<Night[]>(() => NIGHTS_URL);

  protected readonly report = httpResource<NightMetrics>(() => {
    const id = this.nightId();

    return id === undefined ? undefined : metricsUrl(id);
  });

  /** The phases are read against the moment the nights arrived, as the listing does. */
  private readonly now = computed(() => (this.nights.hasValue() ? new Date() : null));

  protected readonly choices = computed(() => {
    const now = this.now();

    if (now === null || !this.nights.hasValue()) return [];

    return reportableNights(this.nights.value(), now);
  });

  protected readonly night = computed(() =>
    this.nights.hasValue()
      ? (this.nights.value().find((n) => n.id === this.nightId()) ?? null)
      : null,
  );

  protected readonly isLive = computed(() => {
    const night = this.night();
    const now = this.now();

    return night !== null && now !== null && phaseOf(night, now) === 'underway';
  });

  protected readonly hours = computed(() => {
    const night = this.night();

    return night === null ? '' : nightHours(night);
  });

  /** The tab opened on its own, and no night has started yet. */
  protected readonly isEmpty = computed(
    () => this.nightId() === undefined && this.nights.hasValue() && this.choices().length === 0,
  );

  protected readonly failure = computed<LoadFailure>(() => {
    const error = this.report.error() ?? this.nights.error();

    if (error === undefined) return 'none';

    return problemTypeOf(error) === ProblemTypes.nightNotFound ? 'notFound' : 'unreachable';
  });

  protected readonly methods = computed(() => {
    if (!this.report.hasValue()) return [];

    const { byMethod, sold } = this.report.value();

    return byMethod.map((row) => ({
      ...row,
      name: METHOD_NAMES[row.method],
      tone: METHOD_TONES[row.method],
      share: sold === 0 ? 0 : (row.amount / sold) * 100,
    }));
  });

  /** The ranking's bars, each against the most ordered product. */
  protected readonly top = computed(() => {
    if (!this.report.hasValue()) return [];

    const { top } = this.report.value();
    const most = top[0]?.units ?? 0;

    return top.map((product, index) => ({
      ...product,
      rank: index + 1,
      share: most === 0 ? 0 : (product.units / most) * 100,
    }));
  });

  protected readonly price = formatPrice;

  protected readonly minutes = formatMinutes;

  constructor() {
    // Opened from the tab: straight on to the night that matters, replacing
    // the address so Back does not bounce through here again.
    effect(() => {
      if (this.nightId() !== undefined || !this.nights.hasValue()) return;

      const night = nightToOpen(this.nights.value(), new Date());

      if (night !== null) this.open(night.id, true);
    });

    // A night still on keeps moving; a finished one never changes again.
    effect((onCleanup) => {
      if (!this.isLive()) return;

      const refresh = setInterval(() => this.report.reload(), REPORT_REFRESH_MS);

      onCleanup(() => clearInterval(refresh));
    });
  }

  protected choose(event: Event): void {
    this.open((event.target as HTMLSelectElement).value, false);
  }

  protected retry(): void {
    if (this.nights.error() !== undefined) this.nights.reload();
    if (this.report.error() !== undefined) this.report.reload();
  }

  private open(nightId: string, replaceUrl: boolean): void {
    void this.router.navigate(['/', this.venueSlug(), 'staff', 'reports', nightId], { replaceUrl });
  }
}
