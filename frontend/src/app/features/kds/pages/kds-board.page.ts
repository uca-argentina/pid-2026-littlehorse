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
import { NgTemplateOutlet } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { Observable } from 'rxjs';
import { SessionStorage } from '../../../core/auth/session-storage';
import { KDS_RETRY_MS, KdsBoardChannel } from '../../../core/kds/kds-board-channel';
import { VenueBrand } from '../../../shared/venue-brand/venue-brand';
import type { AgeBand } from '../kds-age';
import { ageBandFor, ageLabelFor } from '../kds-age';
import { KdsOrdersService } from '../kds-orders.service';
import { KDS_QUEUE_URL } from '../kds-queue';
import type { KdsQueueOrder } from '../kds-queue';

/** §11: the bar may pick among the next ten in the queue, not the whole night. */
const CHOOSABLE = 10;

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
  imports: [NgTemplateOutlet, VenueBrand],
  styleUrl: './kds-board.page.scss',
  templateUrl: './kds-board.page.html',
})
export class KdsBoardPage {
  readonly venueSlug = input.required<string>();

  protected readonly channel = inject(KdsBoardChannel);

  private readonly kdsOrders = inject(KdsOrdersService);

  private readonly destroyRef = inject(DestroyRef);

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

  /** Orders with a request in flight: their buttons stay pressed until it lands. */
  private readonly busy = signal<ReadonlySet<string>>(new Set());

  /** What the last action on an order could not do, said to the bar. */
  protected readonly actionFailure = signal<string | null>(null);

  /**
   * The next ten, oldest paid first — the order the queue already arrives in.
   * Everything newer waits until it moves up.
   */
  private readonly choosable = computed(
    () =>
      new Set(
        (this.orders() ?? [])
          .filter((order) => order.status === 'Queued')
          .slice(0, CHOOSABLE)
          .map((order) => order.code),
      ),
  );

  /**
   * The codes somebody tapped to take together (US-16, criterion 2). An order
   * that leaves the next ten — taken, or moved on another reload — is
   * forgotten, not just hidden: if it came back, an old tap nobody remembers
   * must not take it.
   */
  private readonly picked = linkedSignal<ReadonlySet<string>, ReadonlySet<string>>({
    source: () => this.choosable(),
    computation: (choosable, previous) =>
      new Set([...(previous?.value ?? [])].filter((code) => choosable.has(code))),
  });

  protected readonly chosen = computed(() =>
    (this.orders() ?? []).filter((order) => this.picked().has(order.code)),
  );

  /** Every drink of the chosen orders, summed: what the bartender is about to make together. */
  protected readonly chosenDrinks = computed(() => {
    const totals = new Map<string, number>();

    for (const line of this.chosen().flatMap((order) => order.lines)) {
      totals.set(line.productName, (totals.get(line.productName) ?? 0) + line.quantity);
    }

    return [...totals].map(([name, quantity]) => `${quantity}× ${name}`);
  });

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

    this.destroyRef.onDestroy(() => {
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

  protected canChoose(order: KdsQueueOrder): boolean {
    return this.choosable().has(order.code);
  }

  protected isChosen(order: KdsQueueOrder): boolean {
    return this.chosen().some((chosen) => chosen.code === order.code);
  }

  protected isBusy(order: KdsQueueOrder): boolean {
    return this.busy().has(order.code);
  }

  protected toggle(order: KdsQueueOrder): void {
    this.picked.update((picked) => {
      const next = new Set(picked);

      if (next.has(order.code)) next.delete(order.code);
      else next.add(order.code);

      return next;
    });
  }

  protected clearChoice(): void {
    this.picked.set(new Set());
  }

  /** The card's own "Imprimir": that order alone. */
  protected take(order: KdsQueueOrder): void {
    this.act(order.code, this.kdsOrders.startPreparing(order.code), 'tomar el pedido');
  }

  /**
   * One request per order, never one combined: each is taken on its own, and
   * one that fails leaves the rest taken (US-16, criterion 2).
   */
  protected takeChosen(): void {
    for (const order of this.chosen()) this.take(order);
  }

  protected returnToQueue(order: KdsQueueOrder): void {
    this.act(order.code, this.kdsOrders.returnToQueue(order.code), 'devolver a la cola el pedido');
  }

  private act(code: string, request: Observable<void>, what: string): void {
    this.actionFailure.set(null);
    this.busy.update((busy) => new Set(busy).add(code));

    const settle = (): void => {
      this.busy.update((busy) => {
        const next = new Set(busy);
        next.delete(code);

        return next;
      });
      this.picked.update((picked) => {
        const next = new Set(picked);
        next.delete(code);

        return next;
      });
    };

    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        settle();
        this.queue.reload();
      },
      // A failed action usually means the card was stale — the order moved
      // on elsewhere — so the queue is read again to show what it is now.
      error: () => {
        settle();
        this.actionFailure.set(`No pudimos ${what} ${code}. Probá de nuevo.`);
        this.queue.reload();
      },
    });
  }
}
