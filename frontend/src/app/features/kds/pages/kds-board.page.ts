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
import { Router } from '@angular/router';
import type { Observable } from 'rxjs';
import { SessionStorage } from '../../../core/auth/session-storage';
import { KDS_RETRY_MS, KdsBoardChannel } from '../../../core/kds/kds-board-channel';
import { slowLoading } from '../../../shared/loading/slow-loading';
import { VenueBrand } from '../../../shared/venue-brand/venue-brand';
import type { AgeBand } from '../kds-age';
import { ageBandFor, ageLabelFor } from '../kds-age';
import { byTimeInColumn, matchesSearch, minutesInColumn, readyShelf } from '../kds-board-view';
import { KdsOrdersService } from '../kds-orders.service';
import { KDS_QUEUE_URL } from '../kds-queue';
import type { KdsOrderStatus, KdsQueueOrder } from '../kds-queue';

/** How often the clock ticks to re-read every card's age, independent of any SignalR message. */
const AGE_TICK_MS = 15_000;

type ColumnKey = KdsOrderStatus;

interface Column {
  readonly key: ColumnKey;
  readonly title: string;
  /** What is drawn: in Listos, only the most recently made unless searching. */
  readonly orders: KdsQueueOrder[];
  /** What the column's number says: every order in it, drawn or not. */
  readonly total: number;
  /** Ready orders out of view, the ones waiting longest (US-18). */
  readonly hidden: number;
}

const COLUMN_TITLES: Record<ColumnKey, string> = {
  Queued: 'Nuevos',
  InPreparation: 'En preparación',
  Ready: 'Listos en la barra',
};

/**
 * Which action on which order did not go through. Data and not a sentence: the
 * template says it, in Spanish, like every other text the bar reads.
 */
interface ActionFailure {
  readonly action: 'take' | 'return' | 'ready' | 'backToPreparation' | 'deliver' | 'undo';
  readonly code: string;
}

/** How long "Entregado A-0066 · Deshacer" stays on screen after a delivery (US-18). */
const UNDO_OFFER_MS = 10_000;

/** Where a column's scroll thumb is drawn, and whether it is showing. */
interface ScrollThumb {
  readonly top: number;
  readonly height: number;
  readonly visible: boolean;
}

/** How long after the column stops moving its thumb fades out, like Safari's. */
const THUMB_FADE_AFTER_MS = 1_000;

/** Never shorter than this, so a thumb over a very long column is still seen. */
const THUMB_MIN_PX = 32;

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

  private readonly router = inject(Router);

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

  /**
   * Nothing has arrived yet and nothing has failed: the board draws its own
   * outline. Only the first load — a reload keeps the cards that are there.
   */
  protected readonly isFirstLoad = computed(() => this.orders() === null && !this.failed());

  protected readonly isSlow = slowLoading(() => this.isFirstLoad());

  /** The three columns drawn empty, while the first queue is on its way. */
  protected readonly skeletonColumns = (['Queued', 'InPreparation', 'Ready'] as const).map(
    (key) => ({ key, title: COLUMN_TITLES[key] }),
  );

  /** How many ghost cards each column gets: Nuevos is the one that fills up. */
  protected readonly skeletonCards: Record<ColumnKey, number[]> = {
    Queued: [1, 2, 3],
    InPreparation: [1, 2],
    Ready: [1],
  };

  /** What is typed in the board's search: number or name (US-18, criterion 3). */
  protected readonly search = signal('');

  protected readonly columns = computed<Column[]>(() => {
    const query = this.search();
    const searching = query.trim() !== '';
    const matching = (this.orders() ?? []).filter((order) => matchesSearch(order, query));

    return (['Queued', 'InPreparation', 'Ready'] as const).map((key) => {
      // Nuevos arrives oldest paid first; the others go by their own clock.
      const inStatus = matching.filter((order) => order.status === key);
      const inColumn = key === 'Queued' ? inStatus : byTimeInColumn(inStatus);

      if (key !== 'Ready')
        return {
          key,
          title: COLUMN_TITLES[key],
          orders: inColumn,
          total: inColumn.length,
          hidden: 0,
        };

      // A search is how an order out of view is found, so it shows them all.
      const shelf = readyShelf(inColumn, searching ? inColumn.length : undefined);

      return {
        key,
        title: COLUMN_TITLES[key],
        orders: shelf.shown,
        total: inColumn.length,
        hidden: shelf.hidden,
      };
    });
  });

  protected readonly isEmpty = computed(() => this.orders()?.length === 0);

  /** Orders with a request in flight: their buttons stay pressed until it lands. */
  private readonly busy = signal<ReadonlySet<string>>(new Set());

  /** Columns scrolled more than a screen down, each on its own. */
  private readonly farDown = signal<ReadonlySet<ColumnKey>>(new Set());

  /**
   * Where each column's scroll thumb sits, while it is moving. The native bar
   * is hidden — in Chrome on a desktop it takes width from the cards — and this
   * stands in for Safari's overlay one: it shows while the column scrolls and
   * fades out once it stops.
   */
  private readonly thumbs = signal<ReadonlyMap<ColumnKey, ScrollThumb>>(new Map());

  private readonly thumbTimers = new Map<ColumnKey, ReturnType<typeof setTimeout>>();

  /** What the last action on an order could not do, said to the bar. */
  protected readonly actionFailure = signal<ActionFailure | null>(null);

  /**
   * Every order in Nuevos, and every one in preparation (US-18: several made
   * together are marked ready together). Decided on 2026-09-28, against §11's
   * "next ten": a limit nothing on screen showed looked like a broken tap.
   */
  private readonly choosable = computed(
    () =>
      new Set(
        (this.orders() ?? [])
          .filter((order) => order.status === 'Queued' || order.status === 'InPreparation')
          .map((order) => order.code),
      ),
  );

  /**
   * The codes somebody tapped to act on together (US-16, US-18), each with the
   * column it was chosen in. An order that leaves that column — taken, marked
   * ready, or moved on another reload — is forgotten, not just hidden: if it
   * came back, an old tap nobody remembers must not act on it.
   */
  private readonly picked = linkedSignal<KdsQueueOrder[] | null, ReadonlyMap<string, ColumnKey>>({
    source: () => this.orders(),
    computation: (orders, previous) => {
      const statusNow = new Map((orders ?? []).map((order) => [order.code, order.status]));

      return new Map(
        [...(previous?.value ?? [])].filter(([code, column]) => statusNow.get(code) === column),
      );
    },
  });

  protected readonly chosen = computed(() =>
    (this.orders() ?? []).filter((order) => this.picked().has(order.code)),
  );

  /** The one column the chosen orders are in, which decides the bar's action. */
  protected readonly chosenColumn = computed<ColumnKey | null>(
    () => this.chosen()[0]?.status ?? null,
  );

  /** The order just delivered by hand, while its "Deshacer" is on offer. */
  protected readonly justDelivered = signal<string | null>(null);

  private undoOffer: ReturnType<typeof setTimeout> | null = null;

  /** Every drink of the chosen orders, summed: what the bartender is about to make together. */
  protected readonly chosenDrinks = computed(() => {
    const totals = new Map<string, number>();

    for (const item of this.chosen().flatMap((order) => order.orderItems)) {
      totals.set(item.productName, (totals.get(item.productName) ?? 0) + item.quantity);
    }

    return [...totals].map(([name, quantity]) => `${quantity}× ${name}`);
  });

  constructor() {
    this.channel.connect(() => this.queue.reload());

    // US-32: the session ends while the board is open — the API or the hub
    // refused the token — and nobody navigates on a tablet behind the bar, so
    // the guard that would send them to sign in never runs. The board does it.
    effect(() => {
      if (this.sessions.session() !== null) return;

      void this.router.navigate(['/', this.venueSlug(), 'staff', 'login'], {
        queryParams: this.sessions.expired() ? { expired: true } : {},
      });
    });

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
      for (const timer of this.thumbTimers.values()) clearTimeout(timer);
      if (this.undoOffer !== null) clearTimeout(this.undoOffer);
      this.channel.disconnect();
    });
  }

  /** Minutes in its current column: the clock restarts when it moves (2026-09-28). */
  protected ageMinutes(order: KdsQueueOrder): number {
    return minutesInColumn(order, this.now());
  }

  /** Nuevos and En preparación warn at 5 and 10 minutes; a ready card has no color. */
  protected ageBand(order: KdsQueueOrder): AgeBand {
    return order.status === 'Ready' ? 'ok' : ageBandFor(this.ageMinutes(order));
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

  /** One column at a time: choosing in another column lets go of the first. */
  protected toggle(order: KdsQueueOrder): void {
    const otherColumn = this.chosenColumn() !== null && this.chosenColumn() !== order.status;

    this.picked.update((picked) => {
      const next = new Map(otherColumn ? [] : picked);

      if (next.has(order.code)) next.delete(order.code);
      else next.set(order.code, order.status);

      return next;
    });
  }

  /**
   * Called on every scroll of one column's list. More than one screen of that
   * column down is "far": the way back to its oldest orders shows up.
   */
  protected onColumnScroll(key: ColumnKey, stack: HTMLElement): void {
    this.showThumb(key, stack);

    const far = stack.scrollTop > stack.clientHeight;

    if (far === this.farDown().has(key)) return;

    this.farDown.update((columns) => {
      const next = new Set(columns);

      if (far) next.add(key);
      else next.delete(key);

      return next;
    });
  }

  protected thumbOf(key: ColumnKey): ScrollThumb | undefined {
    return this.thumbs().get(key);
  }

  /** Sized like a native thumb: as tall as the share of the column in view. */
  private showThumb(key: ColumnKey, stack: HTMLElement): void {
    const scrollable = stack.scrollHeight - stack.clientHeight;

    if (scrollable <= 0) return;

    const height = Math.max(
      THUMB_MIN_PX,
      (stack.clientHeight / stack.scrollHeight) * stack.clientHeight,
    );
    const top = stack.offsetTop + (stack.scrollTop / scrollable) * (stack.clientHeight - height);

    this.setThumb(key, { top, height, visible: true });

    const running = this.thumbTimers.get(key);
    if (running !== undefined) clearTimeout(running);

    this.thumbTimers.set(
      key,
      setTimeout(() => {
        const thumb = this.thumbs().get(key);
        if (thumb) this.setThumb(key, { ...thumb, visible: false });
      }, THUMB_FADE_AFTER_MS),
    );
  }

  private setThumb(key: ColumnKey, thumb: ScrollThumb): void {
    this.thumbs.update((thumbs) => new Map(thumbs).set(key, thumb));
  }

  protected isFarDown(key: ColumnKey): boolean {
    return this.farDown().has(key);
  }

  /** Smooth, so the bartender sees the column move rather than jump. */
  protected backToTop(stack: HTMLElement): void {
    stack.scrollTo({ top: 0, behavior: 'smooth' });
  }

  protected clearChoice(): void {
    this.picked.set(new Map());
  }

  /** The card's own "Preparar": that order alone. */
  protected take(order: KdsQueueOrder): void {
    this.act(order.code, this.kdsOrders.startPreparing(order.code), 'take');
  }

  /**
   * One request per order, never one combined: each is taken on its own, and
   * one that fails leaves the rest taken (US-16, criterion 2).
   */
  protected takeChosen(): void {
    for (const order of this.chosen()) this.take(order);
  }

  protected returnToQueue(order: KdsQueueOrder): void {
    this.act(order.code, this.kdsOrders.returnToQueue(order.code), 'return');
  }

  /** US-18, criterion 1: "Listo" on a card in preparation. */
  protected markReady(order: KdsQueueOrder): void {
    this.act(order.code, this.kdsOrders.markReady(order.code), 'ready');
  }

  /** One request per order, as with Preparar. */
  protected markChosenReady(): void {
    for (const order of this.chosen()) this.markReady(order);
  }

  protected returnToPreparation(order: KdsQueueOrder): void {
    this.act(order.code, this.kdsOrders.returnToPreparation(order.code), 'backToPreparation');
  }

  /** "Entregado" by hand, with a few seconds to undo a mistaken tap. */
  protected deliver(order: KdsQueueOrder): void {
    this.act(order.code, this.kdsOrders.deliver(order.code), 'deliver', () =>
      this.offerUndo(order.code),
    );
  }

  protected undoDelivery(code: string): void {
    this.withdrawUndo();
    this.act(code, this.kdsOrders.undoDelivery(code), 'undo');
  }

  private offerUndo(code: string): void {
    this.withdrawUndo();
    this.justDelivered.set(code);
    this.undoOffer = setTimeout(() => this.withdrawUndo(), UNDO_OFFER_MS);
  }

  private withdrawUndo(): void {
    if (this.undoOffer !== null) clearTimeout(this.undoOffer);
    this.undoOffer = null;
    this.justDelivered.set(null);
  }

  private act(
    code: string,
    request: Observable<void>,
    action: ActionFailure['action'],
    done?: () => void,
  ): void {
    this.actionFailure.set(null);
    this.busy.update((busy) => new Set(busy).add(code));

    const settle = (): void => {
      this.busy.update((busy) => {
        const next = new Set(busy);
        next.delete(code);

        return next;
      });
      this.picked.update((picked) => {
        const next = new Map(picked);
        next.delete(code);

        return next;
      });
    };

    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        settle();
        done?.();
        this.queue.reload();
      },
      // A failed action usually means the card was stale — the order moved
      // on elsewhere — so the queue is read again to show what it is now.
      error: () => {
        settle();
        this.actionFailure.set({ action, code });
        this.queue.reload();
      },
    });
  }
}
