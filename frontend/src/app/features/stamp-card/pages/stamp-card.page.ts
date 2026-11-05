import { httpResource } from '@angular/common/http';
import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { problemTypeOf } from '../../../core/api/problem-type-of';
import { ProblemTypes } from '../../../core/api/problem-types';
import type { StampCardView } from '../../../core/stamp-card/stamp-cards';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { AuditNote } from '../../../shared/audit-note/audit-note';
import { StampProgress } from '../../../shared/stamp-progress/stamp-progress';
import { PRODUCTS_URL } from '../../products/products.service';
import type { Product } from '../../products/products.service';
import {
  LEAST_STAMPS,
  MOST_STAMPS,
  STAMP_CARD_RULE_URL,
  StampCardRuleService,
} from '../stamp-card-rule.service';
import type { StampCardRule } from '../stamp-card-rule.service';

type Outcome = 'none' | 'saved' | 'productGone' | 'combo' | 'invalid' | 'unreachable';

/** What a venue starts from when it has no card yet. */
const DEFAULT_STAMPS = '5';

/**
 * US-57: the administrator sets up the stamp card: how many stamps earn the
 * reward, which product is given, and whether it is on. One per venue. Laid out
 * for a computer: the rule on the left and, beside it, what the customer will
 * see with those numbers.
 */
@Component({
  selector: 'drinkit-stamp-card-page',
  imports: [AdminHeader, AuditNote, StampProgress],
  styleUrls: ['../../../shared/admin-desk/admin-desk.scss', './stamp-card.page.scss'],
  templateUrl: './stamp-card.page.html',
})
export class StampCardPage {
  private readonly service = inject(StampCardRuleService);

  private readonly destroyRef = inject(DestroyRef);

  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  protected readonly products = httpResource<Product[]>(() => PRODUCTS_URL);

  // No card yet answers with no content: the resource then holds null.
  protected readonly rule = httpResource<StampCardRule | null>(() => STAMP_CARD_RULE_URL);

  protected readonly stamps = signal(DEFAULT_STAMPS);

  protected readonly rewardId = signal('');

  protected readonly isActive = signal(true);

  protected readonly attempted = signal(false);

  protected readonly isSending = signal(false);

  protected readonly outcome = signal<Outcome>('none');

  /** The audit of the rule as last saved, shown at the foot. */
  protected readonly saved = signal<StampCardRule | null>(null);

  /** Filled once, when the rule arrives: a reload must not overwrite what is being typed. */
  private filled = false;

  protected readonly hasNoCardYet = computed(
    () => this.rule.hasValue() && this.rule.value() === null && this.saved() === null,
  );

  /** On the menu and not a combo: what a reward can be. */
  protected readonly choices = computed(() =>
    (this.products.hasValue() ? this.products.value() : [])
      .filter((product) => product.isActive && !product.isCombo)
      .sort((one, other) => one.name.localeCompare(other.name, 'es-AR')),
  );

  protected readonly stampsError = computed(() => {
    const count = Number(this.stamps());

    return this.attempted() &&
      !(Number.isInteger(count) && count >= LEAST_STAMPS && count <= MOST_STAMPS)
      ? `Los sellos tienen que ser un número entero de ${LEAST_STAMPS} a ${MOST_STAMPS}.`
      : null;
  });

  protected readonly rewardError = computed(() =>
    this.attempted() && this.rewardId() === '' ? 'Elegí el producto de regalo.' : null,
  );

  /** What the customer sees with what is typed: a card with nothing stamped yet. */
  protected readonly preview = computed<StampCardView | null>(() => {
    const count = Number(this.stamps());
    const reward = this.choices().find((product) => product.id === this.rewardId());

    if (!Number.isInteger(count) || count < LEAST_STAMPS || reward === undefined) return null;

    return {
      isActive: true,
      stampsForReward: count,
      rewardProductId: reward.id,
      rewardName: reward.name,
      stamps: 0,
      canRedeem: false,
    };
  });

  constructor() {
    effect(() => {
      if (this.filled || !this.rule.hasValue()) return;

      const rule = this.rule.value();

      this.filled = true;

      if (rule === null) return;

      this.stamps.set(String(rule.stampsForReward));
      this.rewardId.set(rule.rewardProductId);
      this.isActive.set(rule.isActive);
      this.saved.set(rule);
    });
  }

  protected read(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected toggleActive(): void {
    this.isActive.update((on) => !on);
  }

  protected submit(): void {
    this.attempted.set(true);

    if (this.isSending() || this.stampsError() !== null || this.rewardError() !== null) return;

    this.isSending.set(true);
    this.outcome.set('none');

    this.service
      .save({
        stampsForReward: Number(this.stamps()),
        rewardProductId: this.rewardId(),
        isActive: this.isActive(),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (rule) => {
          this.isSending.set(false);
          this.saved.set(rule);
          this.outcome.set('saved');
        },
        error: (error: unknown) => {
          this.isSending.set(false);
          this.outcome.set(reasonFor(error));
        },
      });
  }
}

function reasonFor(error: unknown): Outcome {
  const type = problemTypeOf(error);

  if (type === ProblemTypes.stampCardRewardProductNotFound) return 'productGone';
  if (type === ProblemTypes.stampCardRewardIsACombo) return 'combo';

  return type?.startsWith('urn:drinkit:problem:stamp-card') === true ? 'invalid' : 'unreachable';
}
