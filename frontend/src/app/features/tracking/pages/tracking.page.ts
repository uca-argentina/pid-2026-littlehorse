import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { QRCodeComponent } from 'angularx-qrcode';
import type { CustomerOrderStatus } from '../tracking.service';
import { TrackingStore } from '../tracking.store';

/** One step of the journey, as the screen draws it. */
interface Step {
  readonly name: string;
  readonly reached: boolean;
}

/**
 * The four steps of the journey, and which server statuses count as having
 * reached each one.
 *
 * Paid and Queued are one step: for somebody holding a phone they are the same
 * thing — already paid, still waiting. Delivered never arrives as an answer —
 * the API stops showing an order once it is handed over — so the screen supplies
 * it itself when the link stops working on an order it was already showing.
 */
const JOURNEY: readonly { name: string; after: readonly CustomerOrderStatus[] }[] = [
  {
    // Not "esperando en la barra": that read as the drink waiting at the bar.
    name: 'En cola',
    after: ['Paid', 'Queued', 'InPreparation', 'Ready', 'Delivered'],
  },
  { name: 'En preparación', after: ['InPreparation', 'Ready', 'Delivered'] },
  { name: 'Listo', after: ['Ready', 'Delivered'] },
  { name: 'Entregado', after: ['Delivered'] },
];

/** What to say about each status, in the words somebody in a bar would use. */
const WHAT_IS_HAPPENING: Partial<Record<CustomerOrderStatus, string>> = {
  AwaitingPayment: 'Falta pagar en la caja. Recién ahí lo empiezan a preparar.',
  Paid: 'Ya está pago y en la cola. Te avisamos cuando lo empiecen a preparar.',
  Queued: 'Ya está pago y en la cola. Te avisamos cuando lo empiecen a preparar.',
  InPreparation: 'Lo están preparando.',
  Ready: '¡Está listo! Acercate a la barra y mostrá tu QR.',
  Delivered: 'Entregado. ¡Que lo disfrutes!',
};

/**
 * The statuses in which there is something to pick up at the bar with the QR:
 * paid and not yet handed over. Before paying, the cashier needs the code, not
 * this; once handed over, it must not look like it still claims anything.
 */
const CLAIMABLE: readonly CustomerOrderStatus[] = [
  'AwaitingPayment',
  'Paid',
  'Queued',
  'InPreparation',
  'Ready',
];

/**
 * Colours of the QR itself, which the library takes as values rather than
 * CSS: the same paper and ink as the block around it (--dk-paper and
 * --dk-on-paper), so the quiet zone and the block read as one light surface.
 */
const QR_PAPER = '#f7f4ed';

const QR_INK = '#12100e';

/**
 * Where somebody's order is.
 *
 * The customer's screen that changes without anybody touching it: it hears the
 * order move over a live link (US-22) and asks again, which is what lets
 * somebody stay at their table instead of standing at the bar.
 *
 * It is also the screen somebody lands on the moment they pay, so it opens with
 * the code big enough to read across a room — that is the first thing they need
 * — and the journey underneath.
 */
@Component({
  selector: 'drinkit-tracking-page',
  imports: [RouterLink, QRCodeComponent],
  providers: [TrackingStore],
  styleUrl: './tracking.page.scss',
  templateUrl: './tracking.page.html',
})
export class TrackingPage {
  protected readonly store = inject(TrackingStore);

  readonly venueSlug = input.required<string>();

  /** From the path: the order's name, "K-4821". */
  readonly code = input.required<string>();

  /** From the path: the secret that says this order is theirs. */
  readonly token = input.required<string>();

  protected readonly menuLink = computed(() => ['/', this.venueSlug(), 'menu']);

  /**
   * The status to draw, which is not always the last one the server sent.
   *
   * An order that finished stops being shown at all, so the end of the journey
   * arrives as the link going dead rather than as a status. Somebody who has
   * been watching their order deserves to see it arrive, not an alert saying
   * their link is broken at the moment their drinks reached them.
   */
  private readonly status = computed<CustomerOrderStatus | null>(() =>
    this.store.status() === 'over' ? 'Delivered' : (this.store.order()?.status ?? null),
  );

  /** Paying in cash: the code is for the cashier first, and the bar only after (US-25). */
  protected readonly paysAtTheTill = computed(() => this.status() === 'AwaitingPayment');

  /** US-23: nothing to pick up and nothing left to wait for, so the journey is not drawn. */
  protected readonly isCanceled = computed(() => this.status() === 'Canceled');

  /** "Cancelar pedido" was tapped once: it asks before doing it. */
  protected readonly confirmingCancel = signal(false);

  protected readonly whatIsHappening = computed(() => {
    const status = this.status();

    return status === null ? '' : (WHAT_IS_HAPPENING[status] ?? '');
  });

  /**
   * US-20: the QR, while there is something to pay for at the till (the cashier
   * scans it, decided on 2026-09-29) or to pick up at the bar.
   */
  protected readonly showsQr = computed(() => {
    const status = this.status();

    return status !== null && CLAIMABLE.includes(status);
  });

  protected readonly qrPaper = QR_PAPER;

  protected readonly qrInk = QR_INK;

  protected readonly steps = computed<Step[]>(() =>
    JOURNEY.map((step) => {
      const status = this.status();

      return { name: step.name, reached: status !== null && step.after.includes(status) };
    }),
  );

  constructor() {
    effect(() => {
      const venueSlug = this.venueSlug();
      const code = this.code();
      const token = this.token();

      // Only the address is worth reacting to. Following reads the store's own
      // signals on the way in, and tracking those would make every answer start
      // the polling over again — a loop that feeds itself, one timer per round.
      untracked(() => this.store.follow(venueSlug, code, token));
    });
  }
}
