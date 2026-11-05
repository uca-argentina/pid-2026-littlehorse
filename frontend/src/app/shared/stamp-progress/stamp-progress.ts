import { Component, computed, input } from '@angular/core';
import type { StampCardView } from '../../core/stamp-card/stamp-cards';

/**
 * Past this many, a row of slots stops fitting a phone and stops being read:
 * the numbers say it instead.
 */
const MOST_SLOTS = 12;

/**
 * The customer's card (US-58): how many stamps out of how many, and which
 * reward they are working toward. Dumb on purpose — the menu and the tracking
 * screen draw the same card from the same answer.
 */
@Component({
  selector: 'drinkit-stamp-progress',
  styleUrl: './stamp-progress.scss',
  templateUrl: './stamp-progress.html',
})
export class StampProgress {
  readonly card = input.required<StampCardView>();

  /** The text equivalent of the slots: they are decoration, this is the information. */
  protected readonly progress = computed(
    () => `${this.card().stamps} de ${this.card().stampsForReward} sellos`,
  );

  protected readonly slots = computed(() =>
    this.card().stampsForReward > MOST_SLOTS
      ? []
      : Array.from(
          { length: this.card().stampsForReward },
          (_, index) => index < this.card().stamps,
        ),
  );
}
