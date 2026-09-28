import { httpResource } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { KdsBoardChannel } from '../../../core/kds/kds-board-channel';
import { ProblemTypes } from '../../../core/api/problem-types';
import { problemTypeOf } from '../../../core/api/problem-type-of';
import type { AgeBand } from '../kds-age';
import { ageBandFor, ageLabelFor } from '../kds-age';
import { KDS_QUEUE_URL } from '../kds-queue';
import type { KdsQueueOrder } from '../kds-queue';

/** How often the clock ticks to re-read every card's age, independent of any SignalR message. */
const AGE_TICK_MS = 15_000;

type ColumnKey = 'Queued' | 'InPreparation' | 'Ready';

interface Column {
  readonly key: ColumnKey;
  readonly title: string;
  readonly orders: KdsQueueOrder[];
}

/** Why the queue is not on screen. */
type LoadFailure = 'none' | 'forbidden' | 'unreachable';

const COLUMN_TITLES: Record<ColumnKey, string> = {
  Queued: 'Nuevos',
  InPreparation: 'En preparación',
  Ready: 'Listos',
};

@Component({
  selector: 'drinkit-kds-board-page',
  styleUrl: './kds-board.page.scss',
  templateUrl: './kds-board.page.html',
})
export class KdsBoardPage {
  private readonly channel = inject(KdsBoardChannel);

  /**
   * The Resource API rather than a subscription: loading, error and value
   * arrive as signals, which is exactly the three states this screen draws.
   */
  protected readonly queue = httpResource<KdsQueueOrder[]>(() => KDS_QUEUE_URL);

  /**
   * Ticks on its own, apart from SignalR: the hub says when an order changed
   * hands, never that five more minutes went by. Without this a card painted
   * green at minute 4 would stay green forever unless something else happened
   * to re-render it.
   */
  private readonly now = signal(Date.now());

  private readonly orders = computed(() => (this.queue.hasValue() ? this.queue.value() : []));

  protected readonly columns = computed<Column[]>(() =>
    (['Queued', 'InPreparation', 'Ready'] as const).map((key) => ({
      key,
      title: COLUMN_TITLES[key],
      orders: this.orders().filter((order) => order.status === key),
    })),
  );

  protected readonly isEmpty = computed(() => this.queue.hasValue() && this.orders().length === 0);

  protected readonly failure = computed<LoadFailure>(() => {
    const error = this.queue.error();

    if (error === undefined) return 'none';

    return problemTypeOf(error) === ProblemTypes.forbidden ? 'forbidden' : 'unreachable';
  });

  constructor() {
    this.channel.connect(() => this.queue.reload());

    const ticking = setInterval(() => this.now.set(Date.now()), AGE_TICK_MS);

    inject(DestroyRef).onDestroy(() => {
      clearInterval(ticking);
      this.channel.disconnect();
    });
  }

  protected ageMinutes(order: KdsQueueOrder): number {
    return Math.max(0, (this.now() - Date.parse(order.paidAt)) / 60_000);
  }

  protected ageBand(order: KdsQueueOrder): AgeBand {
    return ageBandFor(this.ageMinutes(order));
  }

  protected ageLabel(order: KdsQueueOrder): string {
    return ageLabelFor(this.ageMinutes(order));
  }

  protected deliveryLabel(order: KdsQueueOrder): string {
    return order.isForTable ? 'Mesa' : 'Barra';
  }
}
