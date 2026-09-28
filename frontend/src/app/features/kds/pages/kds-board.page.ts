import { httpResource } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  linkedSignal,
  signal,
} from '@angular/core';
import { SessionStorage } from '../../../core/auth/session-storage';
import { KDS_RETRY_MS, KdsBoardChannel } from '../../../core/kds/kds-board-channel';
import { VenueBrand } from '../../../shared/venue-brand/venue-brand';
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

const COLUMN_TITLES: Record<ColumnKey, string> = {
  Queued: 'Nuevos',
  InPreparation: 'En preparación',
  Ready: 'Listos en la barra',
};

@Component({
  selector: 'drinkit-kds-board-page',
  imports: [VenueBrand],
  styleUrl: './kds-board.page.scss',
  templateUrl: './kds-board.page.html',
})
export class KdsBoardPage {
  readonly venueSlug = input.required<string>();

  protected readonly channel = inject(KdsBoardChannel);

  private readonly sessions = inject(SessionStorage);

  /**
   * The station's own account stands in for "Barra principal" until there is
   * a BarStation to name: the account is the station's, not a person's.
   */
  protected readonly station = computed(() => this.sessions.session()?.username ?? '');

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

  /**
   * The last queue that actually arrived, or null before the first one. A
   * resource in error has no value, and drawing that would wipe the board
   * mid-service over a single failed reload.
   */
  protected readonly orders = linkedSignal<KdsQueueOrder[] | undefined, KdsQueueOrder[] | null>({
    source: () => (this.queue.hasValue() ? this.queue.value() : undefined),
    computation: (latest, previous) => latest ?? previous?.value ?? null,
  });

  /**
   * Whether the last attempt failed. Held through the retry's own loading, so
   * the warning does not blink off and on every few seconds while it keeps
   * failing.
   */
  protected readonly failed = linkedSignal<string, boolean>({
    source: () => this.queue.status(),
    computation: (status, previous) => {
      if (status === 'error') return true;
      if (status === 'loading' || status === 'reloading') return previous?.value ?? false;

      return false;
    },
  });

  protected readonly columns = computed<Column[]>(() =>
    (['Queued', 'InPreparation', 'Ready'] as const).map((key) => ({
      key,
      title: COLUMN_TITLES[key],
      orders: (this.orders() ?? []).filter((order) => order.status === key),
    })),
  );

  protected readonly isEmpty = computed(() => this.orders()?.length === 0);

  constructor() {
    this.channel.connect(() => this.queue.reload());

    // Nobody behind the bar has a free hand to tap "Reintentar", and a tablet
    // switched on before the wifi came up must not stay dead.
    effect((onCleanup) => {
      if (this.queue.status() !== 'error') return;

      const retry = setTimeout(() => this.queue.reload(), KDS_RETRY_MS);
      onCleanup(() => clearTimeout(retry));
    });

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
