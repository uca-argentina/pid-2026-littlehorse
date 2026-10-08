import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, forkJoin, map, of } from 'rxjs';
import type { Observable } from 'rxjs';
import { MyOrders } from '../../../core/orders/my-orders';
import type { MyOrder } from '../../../core/orders/my-orders';
import { TrackingService } from '../../tracking/tracking.service';
import type { CustomerOrderStatus } from '../../tracking/tracking.service';
import { OrdersInProgressChannel } from './orders-in-progress-channel';

/** One order to go back to, with what it is doing now. */
interface OrderInProgress {
  readonly code: string;
  readonly token: string;
  readonly link: string[];
  /** Null when the status could not be asked: the way back stays, the status does not. */
  readonly status: CustomerOrderStatus | null;
  readonly says: string;
  readonly isReady: boolean;
}

/** What each status says, in the words somebody in a bar would use. Short: read in passing. */
const SAYS: Partial<Record<CustomerOrderStatus, string>> = {
  AwaitingPayment: 'Pagá en la caja con este código',
  Paid: 'En la cola',
  Queued: 'En la cola',
  InPreparation: 'Lo están preparando',
  Ready: '¡Está listo! Retiralo en la barra',
};

/** Said when the status could not be asked: the order is fine, the wifi is not. */
const UNKNOWN = 'Tocá para ver cómo va';

/**
 * Which order needs the customer first, most urgent first: one waiting at the
 * bar, then one waiting for their cash, then the ones the bar is on.
 */
const URGENCY: readonly (CustomerOrderStatus | null)[] = [
  'Ready',
  'AwaitingPayment',
  'InPreparation',
  'Paid',
  'Queued',
  null,
];

/**
 * US-34: the way back to an order from the menu, after the customer left its
 * tracking screen — on purpose or not.
 *
 * The orders come from this phone (MyOrders, filled by the tracking screen);
 * what each is doing comes from the API, asked once as the menu opens.
 */
@Component({
  selector: 'drinkit-orders-in-progress',
  imports: [RouterLink],
  styleUrl: './orders-in-progress.scss',
  templateUrl: './orders-in-progress.html',
})
export class OrdersInProgress {
  private readonly myOrders = inject(MyOrders);

  private readonly tracking = inject(TrackingService);

  private readonly channel = inject(OrdersInProgressChannel);

  readonly venueSlug = input.required<string>();

  private readonly asked = rxResource({
    params: () => this.venueSlug(),
    stream: ({ params: venueSlug }) => this.askAbout(venueSlug),
  });

  /** Most urgent first. Nothing while it asks: a line that appears late beats one that lies. */
  protected readonly orders = computed<OrderInProgress[]>(() =>
    this.asked.hasValue() ? this.asked.value() : [],
  );

  protected readonly first = computed(() => this.orders()[0]);

  /**
   * The tokens to listen to. Compared by content, so asking again and getting
   * the same orders back does not drop the live link to open it again.
   */
  private readonly followed = computed(() => this.orders().map((order) => order.token), {
    equal: (a, b) => a.join() === b.join(),
  });

  constructor() {
    // Criterion 6: an order that moves asks again, with no reloading. Only the
    // orders on screen: one that is gone has nothing left to say.
    effect(() => {
      const tokens = this.followed();

      untracked(() =>
        tokens.length === 0
          ? this.channel.disconnect()
          : this.channel.follow(tokens, () => this.asked.reload()),
      );
    });

    inject(DestroyRef).onDestroy(() => this.channel.disconnect());
  }

  /** Several orders fold into one line until the customer opens them. */
  protected readonly expanded = signal(false);

  protected toggle(): void {
    this.expanded.update((open) => !open);
  }

  private askAbout(venueSlug: string): Observable<OrderInProgress[]> {
    const remembered = this.myOrders.of(venueSlug);

    // forkJoin of nothing completes without a value, and asking nothing is the point.
    if (remembered.length === 0) return of([]);

    return forkJoin(remembered.map((order) => this.askAboutOne(venueSlug, order))).pipe(
      map((asked) =>
        asked
          .filter((order): order is OrderInProgress => order !== null)
          .sort((a, b) => URGENCY.indexOf(a.status) - URGENCY.indexOf(b.status)),
      ),
    );
  }

  /** Null when the order is gone: handed over, canceled, or a link that no longer leads anywhere. */
  private askAboutOne(
    venueSlug: string,
    { code, token }: MyOrder,
  ): Observable<OrderInProgress | null> {
    const link = ['/', venueSlug, 'orders', code, token];

    return this.tracking.follow(venueSlug, code, token).pipe(
      map((order) => {
        // Criterion 4: canceled — by the customer or at the till — is over,
        // the same as handed over. Nothing to pick up, nothing to pay.
        if (order.status === 'Canceled') {
          this.myOrders.forget(venueSlug, code);
          return null;
        }

        return {
          code,
          token,
          link,
          status: order.status,
          says: SAYS[order.status] ?? UNKNOWN,
          isReady: order.status === 'Ready',
        };
      }),
      catchError((error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 404) {
          this.myOrders.forget(venueSlug, code);
          return of(null);
        }

        return of({ code, token, link, status: null, says: UNKNOWN, isReady: false });
      }),
    );
  }
}
